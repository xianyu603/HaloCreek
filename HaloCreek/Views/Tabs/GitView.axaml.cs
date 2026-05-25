using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using HaloCreek.Models;
using HaloCreek.ViewModels.Tabs;

namespace HaloCreek.Views.Tabs
{
    public partial class GitView : UserControl
    {
        public GitView()
        {
            InitializeComponent();
        }

        private void ChangesList_OnDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (e.Source is Button
                || (e.Source is Visual source && source.FindAncestorOfType<Button>() is not null))
            {
                return;
            }

            if (DataContext is not GitViewModel viewModel
                || ChangesList.SelectedItem is not GitChangeInfo change)
            {
                return;
            }

            if (viewModel.OpenSelectedChangeCommand.CanExecute(change))
            {
                viewModel.OpenSelectedChangeCommand.Execute(change);
                e.Handled = true;
            }
        }
    }
}
