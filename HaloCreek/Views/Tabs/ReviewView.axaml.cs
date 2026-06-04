using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using HaloCreek.Logging;
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

        private void UnreviewedItem_OnContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            if (sender is not Control itemControl)
            {
                Log.Info("Review", $"Unreviewed context requested with sender={FormatObject(sender)}.");
                return;
            }

            if (DataContext is not ReviewViewModel viewModel
                || itemControl.DataContext is not ReviewFilePath file)
            {
                Log.Info(
                    "Review",
                    "Unreviewed context requested but command context is unavailable. "
                    + $"ViewDataContext={FormatObject(DataContext)} "
                    + $"ItemDataContext={FormatObject(itemControl.DataContext)}");
                return;
            }

            var menuItem = new MenuItem
            {
                Header = "Add Reviewed",
                Command = viewModel.AddReviewedCommand,
                CommandParameter = file,
            };
            var contextMenu = new ContextMenu();
            contextMenu.Items.Add(menuItem);

            Log.Info(
                "Review",
                "Unreviewed context menu created. "
                + $"File={file.RelativePath} "
                + $"CommandCanExecute={viewModel.AddReviewedCommand.CanExecute(file)} "
                + $"MenuItemCommand={FormatObject(menuItem.Command)} "
                + $"MenuItemCommandParameter={FormatObject(menuItem.CommandParameter)}");

            contextMenu.Open(itemControl);
            e.Handled = true;
        }

        private static string FormatObject(object? value)
        {
            if (value is null)
            {
                return "<null>";
            }

            return $"{value.GetType().FullName}:{value}";
        }
    }
}
