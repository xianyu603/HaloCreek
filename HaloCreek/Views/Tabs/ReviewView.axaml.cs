using Avalonia.Controls;
using Avalonia.Input;
using HaloCreek.ViewModels.Tabs;

namespace HaloCreek.Views.Tabs
{
    public partial class ReviewView : UserControl
    {
        public ReviewView()
        {
            InitializeComponent();
        }

        private void ClipLocateStatus_OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is not ReviewViewModel viewModel)
            {
                return;
            }

            if (viewModel.ShowClipLocateLineCommand.CanExecute(null))
            {
                viewModel.ShowClipLocateLineCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void FrontSession_OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is not ReviewViewModel viewModel)
            {
                return;
            }

            if (viewModel.ActivateFrontClientCommand.CanExecute(null))
            {
                viewModel.ActivateFrontClientCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
