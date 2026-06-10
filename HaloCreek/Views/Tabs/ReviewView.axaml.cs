using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

        private void ModifiedRootContextMenu_OnOpened(object? sender, RoutedEventArgs e)
        {
            if (DataContext is ReviewViewModel viewModel)
            {
                viewModel.ModifiedGit.SelectedChange = null;
            }
        }

        private void ModifiedChangeContextMenu_OnOpened(object? sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu
                || DataContext is not ReviewViewModel viewModel
                || GetContextMenuChange(menu) is not GitChangeInfo change)
            {
                return;
            }

            viewModel.ModifiedGit.SelectedChange = change;
            menu.ItemsSource = viewModel.ModifiedGit.SelectedFilePathActions;
        }

        private void ModifiedChange_OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (e.Source is Button
                || (e.Source is Visual source && source.FindAncestorOfType<Button>() is not null))
            {
                return;
            }

            if (DataContext is not ReviewViewModel viewModel
                || sender is not Control { DataContext: GitChangeInfo change })
            {
                return;
            }

            viewModel.ModifiedGit.SelectedChange = change;
            if (viewModel.ModifiedGit.OpenSelectedChangeCommand.CanExecute(change))
            {
                viewModel.ModifiedGit.OpenSelectedChangeCommand.Execute(change);
                e.Handled = true;
            }
        }

        private static GitChangeInfo? GetContextMenuChange(ContextMenu menu)
        {
            return menu.PlacementTarget is Control { DataContext: GitChangeInfo targetChange }
                ? targetChange
                : menu.DataContext as GitChangeInfo;
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

        private void DiffUnreviewedAgainstReviewedMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not ReviewViewModel viewModel
                || sender is not Control { DataContext: ReviewFilePath file })
            {
                return;
            }

            if (viewModel.DiffUnreviewedAgainstReviewedCommand.CanExecute(file))
            {
                viewModel.SelectedUnreviewedFile = file;
                viewModel.DiffUnreviewedAgainstReviewedCommand.Execute(file);
                e.Handled = true;
            }
        }

        private void RevertUnreviewedToReviewedMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not ReviewViewModel viewModel
                || sender is not Control { DataContext: ReviewFilePath file })
            {
                return;
            }

            if (viewModel.RevertToReviewedCommand.CanExecute(file))
            {
                viewModel.SelectedUnreviewedFile = file;
                viewModel.RevertToReviewedCommand.Execute(file);
                e.Handled = true;
            }
        }

        private void UnreviewedFile_OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is not ReviewViewModel viewModel
                || sender is not Control { DataContext: ReviewFilePath file })
            {
                return;
            }

            if (viewModel.DiffUnreviewedAgainstReviewedCommand.CanExecute(file))
            {
                viewModel.SelectedUnreviewedFile = file;
                viewModel.DiffUnreviewedAgainstReviewedCommand.Execute(file);
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

        private void DiffReviewedAgainstWorkingTreeMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not ReviewViewModel viewModel
                || sender is not Control { DataContext: ReviewFilePath file })
            {
                return;
            }

            if (viewModel.DiffReviewedAgainstWorkingTreeCommand.CanExecute(file))
            {
                viewModel.SelectedReviewedFile = file;
                viewModel.DiffReviewedAgainstWorkingTreeCommand.Execute(file);
                e.Handled = true;
            }
        }

        private void DiffReviewedAgainstHeadMenuItem_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not ReviewViewModel viewModel
                || sender is not Control { DataContext: ReviewFilePath file })
            {
                return;
            }

            if (viewModel.DiffReviewedAgainstHeadCommand.CanExecute(file))
            {
                viewModel.SelectedReviewedFile = file;
                viewModel.DiffReviewedAgainstHeadCommand.Execute(file);
                e.Handled = true;
            }
        }

        private void ReviewedFile_OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is not ReviewViewModel viewModel
                || sender is not Control { DataContext: ReviewFilePath file })
            {
                return;
            }

            if (viewModel.DiffReviewedAgainstHeadCommand.CanExecute(file))
            {
                viewModel.SelectedReviewedFile = file;
                viewModel.DiffReviewedAgainstHeadCommand.Execute(file);
                e.Handled = true;
            }
        }

    }
}
