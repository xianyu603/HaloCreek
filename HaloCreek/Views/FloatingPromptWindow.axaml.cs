using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HaloCreek.Views
{
    public partial class FloatingPromptWindow : Window
    {
        private bool _allowClose;

        public FloatingPromptWindow()
        {
            InitializeComponent();
            Closing += OnClosing;
            Opened += (_, _) => FocusPromptInput();
            Activated += (_, _) => FocusPromptInput();
            AddHandler(
                InputElement.KeyDownEvent,
                OnKeyDown,
                RoutingStrategies.Bubble);
        }

        public void FocusPromptInput()
        {
            PromptInput.FocusPromptTextBox();
        }

        public void CloseForDispose()
        {
            _allowClose = true;
            Close();
        }

        private void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_allowClose)
            {
                return;
            }

            e.Cancel = true;
            Hide();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None)
            {
                return;
            }

            Hide();
            e.Handled = true;
        }
    }
}
