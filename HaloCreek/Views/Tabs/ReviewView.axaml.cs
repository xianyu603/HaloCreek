using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HaloCreek.Models;
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

        private void AddReviewedMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not ReviewViewModel viewModel
                || sender is not Control { DataContext: ReviewFilePath file })
            {
                return;
            }

            if (viewModel.AddReviewedCommand.CanExecute(file))
            {
                viewModel.SelectedUnreviewedFile = file;
                viewModel.AddReviewedCommand.Execute(file);
                e.Handled = true;
            }
        }

        private void MarkUnreviewedMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not ReviewViewModel viewModel
                || sender is not Control { DataContext: ReviewFilePath file })
            {
                return;
            }

            if (viewModel.MarkUnreviewedCommand.CanExecute(file))
            {
                viewModel.SelectedReviewedFile = file;
                viewModel.MarkUnreviewedCommand.Execute(file);
                e.Handled = true;
            }
        }
    }
}
