using System;
using System.ComponentModel;
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
        private PromptInputViewModel? _subscribedViewModel;

        public PromptInputView()
        {
            InitializeComponent();
            DataContextChanged += PromptInputView_OnDataContextChanged;
            PromptTextBox.AddHandler(
                InputElement.KeyDownEvent,
                PromptTextBox_OnKeyDown,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
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

        private void PromptInputView_OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_subscribedViewModel is not null)
            {
                _subscribedViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            }

            _subscribedViewModel = DataContext as PromptInputViewModel;
            if (_subscribedViewModel is not null)
            {
                _subscribedViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
            }
        }

        private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PromptInputViewModel.IsCompletionOpen)
                || sender is not PromptInputViewModel { IsCompletionOpen: true })
            {
                return;
            }

            CompletionMenu.Margin = GetCompletionMenuMargin();
        }

        private Thickness GetCompletionMenuMargin()
        {
            const double verticalGap = 4;

            var caretBounds = GetCaretBoundsInView();
            var left = Math.Max(0, caretBounds.X);
            var top = Math.Max(0, caretBounds.Y + Math.Max(0, caretBounds.Height) + verticalGap);
            return new Thickness(left, top, 0, 0);
        }

        private Rect GetCaretBoundsInView()
        {
            var presenter = PromptTextBox.FindDescendantOfType<TextPresenter>();
            if (presenter is null)
            {
                return new Rect(PromptTextBox.Padding.Left, PromptTextBox.Padding.Top, 0, PromptTextBox.FontSize);
            }

            var textLength = PromptTextBox.Text?.Length ?? 0;
            var caretIndex = Math.Clamp(PromptTextBox.CaretIndex, 0, textLength);
            var caretBounds = presenter.TextLayout.HitTestTextPosition(caretIndex);
            var caretTopLeft = presenter.TranslatePoint(new Point(caretBounds.X, caretBounds.Y), this);

            return caretTopLeft is { } point
                ? new Rect(point, caretBounds.Size)
                : new Rect(PromptTextBox.Padding.Left, PromptTextBox.Padding.Top, 0, PromptTextBox.FontSize);
        }
    }
}
