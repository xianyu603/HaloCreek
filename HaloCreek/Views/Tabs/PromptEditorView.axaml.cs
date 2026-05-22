using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using HaloCreek.Models;
using HaloCreek.ViewModels.Tabs;

namespace HaloCreek.Views.Tabs
{
    public partial class PromptEditorView : UserControl
    {
        public PromptEditorView()
        {
            InitializeComponent();
        }

        private void OngoingSessionsList_OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (e.Source is Button
                || (e.Source is Visual source && source.FindAncestorOfType<Button>() is not null))
            {
                return;
            }

            if (DataContext is not PromptEditorViewModel viewModel
                || OngoingSessionsList.SelectedItem is not OngoingSessionInfo session)
            {
                return;
            }

            if (viewModel.BringToFrontCommand.CanExecute(session))
            {
                viewModel.BringToFrontCommand.Execute(session);
                e.Handled = true;
            }
        }
    }
}
