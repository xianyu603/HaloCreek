using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HaloCreek.ViewModels.Components;

namespace HaloCreek.Views.Components
{
    public partial class PromptInputView : UserControl
    {
        private const double CompletionMenuLeft = 10;
        private const double CompletionMenuCaretGap = 4;
        private const double CompletionMenuBottomPadding = 10;
        private const double CompletionMenuFallbackHeight = 186;

        public PromptInputView()
        {
            InitializeComponent();
            PromptTextBox.AddHandler(
                InputElement.KeyDownEvent,
                PromptTextBox_OnKeyDown,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
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

        private void CompletionMenu_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == IsVisibleProperty && CompletionMenu.IsVisible)
            {
                UpdateCompletionMenuPosition();
            }
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
