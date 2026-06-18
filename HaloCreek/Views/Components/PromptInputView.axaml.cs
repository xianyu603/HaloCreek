using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HaloCreek.ViewModels.Components;

namespace HaloCreek.Views.Components
{
    public partial class PromptInputView : UserControl
    {
        public PromptInputView()
        {
            InitializeComponent();
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
    }
}
