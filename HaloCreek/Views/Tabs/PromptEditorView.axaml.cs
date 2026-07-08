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

        private void OngoingSessionsItems_OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (e.Source is Button
                || (e.Source is Visual source && source.FindAncestorOfType<Button>() is not null))
            {
                return;
            }

            if (DataContext is not PromptEditorViewModel viewModel
                || TryGetOngoingSession(e.Source) is not { } session)
            {
                return;
            }

            if (viewModel.BringToFrontCommand.CanExecute(session))
            {
                viewModel.BringToFrontCommand.Execute(session);
                e.Handled = true;
            }
        }

        private static OngoingSession? TryGetOngoingSession(object? source)
        {
            var visual = source as Visual;
            while (visual is not null)
            {
                if (visual is StyledElement { DataContext: OngoingSession session })
                {
                    return session;
                }

                visual = visual.GetVisualParent();
            }

            return null;
        }
    }
}
