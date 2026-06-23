using System;
using System.Collections.Generic;
using HaloCreek.Models;

namespace HaloCreek.ViewModels.Components
{
    public sealed class PromptTemplatePickerViewModel : ViewModelBase
    {
        private PromptTemplateItem? _previewItem;

        public PromptTemplatePickerViewModel(IReadOnlyList<PromptTemplateItem> items)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
        }

        public IReadOnlyList<PromptTemplateItem> Items { get; }

        public bool HasItems => Items.Count > 0;

        public PromptTemplateItem? PreviewItem
        {
            get => _previewItem;
            private set
            {
                if (SetProperty(ref _previewItem, value))
                {
                    OnPropertyChanged(nameof(IsPreviewVisible));
                    OnPropertyChanged(nameof(PreviewTitle));
                    OnPropertyChanged(nameof(PreviewDescription));
                    OnPropertyChanged(nameof(PreviewBody));
                }
            }
        }

        public bool IsPreviewVisible => PreviewItem is not null;

        public string PreviewTitle => PreviewItem?.Title ?? string.Empty;

        public string PreviewDescription => PreviewItem?.Description ?? string.Empty;

        public string PreviewBody => PreviewItem?.InsertText ?? string.Empty;

        public void ShowPreview(PromptTemplateItem item)
        {
            PreviewItem = item ?? throw new ArgumentNullException(nameof(item));
        }

        public void HidePreview()
        {
            PreviewItem = null;
        }
    }
}
