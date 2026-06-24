using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.ViewModels.Components;

namespace HaloCreek.Views.Components
{
    public partial class PromptInputView : UserControl
    {
        private const double CompletionMenuLeft = 10;
        private const double CompletionMenuCaretGap = 4;
        private const double CompletionMenuBottomPadding = 10;
        private const double CompletionMenuFallbackHeight = 186;
        private const string ClipboardImageTempFileName = "prompt-paste-image.png";
        private const string LogCategory = "PromptInputView";

        public PromptInputView()
        {
            InitializeComponent();
            PromptTextBox.AddHandler(
                InputElement.KeyDownEvent,
                PromptTextBox_OnKeyDown,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            PromptTextBox.PastingFromClipboard += PromptTextBox_OnPastingFromClipboard;
            CompletionMenu.PropertyChanged += CompletionMenu_OnPropertyChanged;
        }

        private void PromptTextBox_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyModifiers != KeyModifiers.None
                || DataContext is not PromptInputViewModel viewModel)
            {
                return;
            }

            if (viewModel.HandleCompletionNavigation(e.Key))
            {
                e.Handled = true;
            }
        }

        public void FocusPromptTextBox()
        {
            PromptTextBox.Focus();
        }

        private async void PromptTextBox_OnPastingFromClipboard(object? sender, RoutedEventArgs e)
        {
            e.Handled = true;

            try
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
                    ?? throw new InvalidOperationException("Clipboard API is not available.");

                var imagePath = await TrySaveClipboardImageAsync(clipboard);
                if (imagePath is not null)
                {
                    InsertRawText(imagePath);
                    return;
                }

                var text = await clipboard.TryGetTextAsync();
                if (text is not null)
                {
                    InsertRawText(text);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(LogCategory, "Clipboard paste failed.");
                Log.Debug(LogCategory, ex.ToString());
            }
        }

        private async Task<string?> TrySaveClipboardImageAsync(IClipboard clipboard)
        {
            using var bitmap = await clipboard.TryGetBitmapAsync();
            if (bitmap is null)
            {
                return null;
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return PlatformInfrastructure.WriteTempFile(ClipboardImageTempFileName, stream.ToArray());
        }

        private void CompletionMenu_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == IsVisibleProperty && CompletionMenu.IsVisible)
            {
                UpdateCompletionMenuPosition();
            }
        }

        private void PromptContextMenu_OnOpened(object? sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu
                || DataContext is not PromptInputViewModel viewModel)
            {
                return;
            }

            var groupMenuItems = viewModel.TemplatePicker.GetGroupsSnapshot()
                .Select(CreateTemplateGroupMenuItem)
                .ToList();

            menu.ItemsSource = groupMenuItems.Count == 0
                ? null
                : groupMenuItems;
        }

        private MenuItem CreateTemplateGroupMenuItem(PromptTemplateItemGroup group)
        {
            var childItems = group.Items
                .Select(CreatePromptMenuItem)
                .ToList();

            return new MenuItem
            {
                Header = group.Title,
                ItemsSource = childItems,
            };
        }

        private void PromptContextMenu_OnClosed(object? sender, RoutedEventArgs e)
        {
            if (DataContext is PromptInputViewModel viewModel)
            {
                viewModel.TemplatePicker.HidePreview();
            }
        }

        private MenuItem CreatePromptMenuItem(PromptTemplateItem templateItem)
        {
            var menuItem = new MenuItem
            {
                Header = templateItem.Title,
                Tag = templateItem,
            };

            menuItem.Click += TemplateMenuItem_OnClick;
            menuItem.PropertyChanged += TemplateMenuItem_OnPropertyChanged;
            return menuItem;
        }

        private void TemplateMenuItem_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != MenuItem.IsSelectedProperty
                || sender is not MenuItem { IsSelected: true, Tag: PromptTemplateItem templateItem }
                || DataContext is not PromptInputViewModel viewModel)
            {
                return;
            }

            viewModel.TemplatePicker.ShowPreview(templateItem);
        }

        private void TemplateMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: PromptTemplateItem templateItem })
            {
                return;
            }

            InsertPromptText(templateItem.InsertText);
            e.Handled = true;
        }

        private void InsertPromptText(string insertText)
        {
            // 这个逻辑写在这里感觉有点职责不清 选择到底是view还是vm管? 但是只为了纯度把选择绑到vm又有点重了
            // 先不想那么多实现在这里 如果未来出现重复再考虑提取的事情
            ReplaceSelection((text, replaceStart, replaceEnd) =>
            {
                var prefix = replaceStart > 0 && !char.IsWhiteSpace(text[replaceStart - 1])
                    ? Environment.NewLine
                    : string.Empty;
                var suffix = replaceEnd < text.Length && !char.IsWhiteSpace(text[replaceEnd])
                    ? Environment.NewLine
                    : string.Empty;
                return (prefix + insertText + suffix, prefix.Length + insertText.Length);
            });
        }

        private void InsertRawText(string insertText)
        {
            ReplaceSelection((_, _, _) => (insertText, insertText.Length));
        }

        private void ReplaceSelection(Func<string, int, int, (string Replacement, int CaretOffset)> createReplacement)
        {
            var text = PromptTextBox.Text ?? string.Empty;
            var selectionStart = Math.Clamp(PromptTextBox.SelectionStart, 0, text.Length);
            var selectionEnd = Math.Clamp(PromptTextBox.SelectionEnd, 0, text.Length);
            var replaceStart = Math.Min(selectionStart, selectionEnd);
            var replaceEnd = Math.Max(selectionStart, selectionEnd);
            var (replacement, caretOffset) = createReplacement(text, replaceStart, replaceEnd);
            var updatedText = text[..replaceStart] + replacement + text[replaceEnd..];
            var caretIndex = replaceStart + caretOffset;

            PromptTextBox.Text = updatedText;
            PromptTextBox.CaretIndex = caretIndex;
            PromptTextBox.SelectionStart = caretIndex;
            PromptTextBox.SelectionEnd = caretIndex;
            PromptTextBox.Focus();
        }

        private void UpdateCompletionMenuPosition()
        {
            var menuHeight = CompletionMenu.Bounds.Height > 0
                ? CompletionMenu.Bounds.Height
                : CompletionMenuFallbackHeight;
            var fallbackTop = Math.Max(0, Bounds.Height - menuHeight - CompletionMenuBottomPadding);
            var textPresenter = PromptTextBox
                .GetVisualDescendants()
                .OfType<TextPresenter>()
                .FirstOrDefault();

            if (textPresenter?.TextLayout is null)
            {
                CompletionMenu.Margin = new Thickness(CompletionMenuLeft, fallbackTop, 0, 0);
                return;
            }

            var caretIndex = Math.Clamp(PromptTextBox.CaretIndex, 0, PromptTextBox.Text?.Length ?? 0);
            var caretRect = textPresenter.TextLayout.HitTestTextPosition(caretIndex);
            var transform = textPresenter.TransformToVisual(this);

            if (transform is null)
            {
                CompletionMenu.Margin = new Thickness(CompletionMenuLeft, fallbackTop, 0, 0);
                return;
            }

            var caretBottom = transform.Value.Transform(new Point(caretRect.X, caretRect.Y + caretRect.Height)).Y;
            var desiredTop = caretBottom + CompletionMenuCaretGap;
            CompletionMenu.Margin = new Thickness(CompletionMenuLeft, Math.Clamp(desiredTop, 0, fallbackTop), 0, 0);
        }
    }
}
