using System;
using Avalonia;
using Avalonia.Controls;
using HaloCreek.ViewModels.Components;

namespace HaloCreek.Views.Components
{
    public partial class PromptInputView : UserControl
    {
        public PromptInputView()
        {
            InitializeComponent();
            PromptTextBox.TextChanged += PromptTextBox_OnTextChanged;
            PromptTextBox.PropertyChanged += PromptTextBox_OnPropertyChanged;
            DataContextChanged += PromptInputView_OnDataContextChanged;
        }

        private void PromptTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
        {
            UpdateCompletionTrigger();
        }

        private void PromptTextBox_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == TextBox.CaretIndexProperty)
            {
                UpdateCompletionTrigger();
            }
        }

        private void PromptInputView_OnDataContextChanged(object? sender, EventArgs e)
        {
            UpdateCompletionTrigger();
        }

        private void UpdateCompletionTrigger()
        {
            if (DataContext is PromptInputViewModel viewModel)
            {
                viewModel.UpdateCompletionTrigger(PromptTextBox.Text, PromptTextBox.CaretIndex);
            }
        }
    }
}
