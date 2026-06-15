using Avalonia.Controls;
using Avalonia.Input;
using HaloCreek.Models;
using HaloCreek.ViewModels.Tabs;

namespace HaloCreek.Views.Tabs
{
    public partial class HistorySessionsView : UserControl
    {
        public HistorySessionsView()
        {
            InitializeComponent();
        }

        private void HistorySession_OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is not HistorySessionsViewModel viewModel
                || sender is not Control { DataContext: HistorySessionInfo session })
            {
                return;
            }

            viewModel.SelectedSession = session;
            if (viewModel.ResumeCommand.CanExecute(session))
            {
                viewModel.ResumeCommand.Execute(session);
                e.Handled = true;
            }
        }
    }
}
