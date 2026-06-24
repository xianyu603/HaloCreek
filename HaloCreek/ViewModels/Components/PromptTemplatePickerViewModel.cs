using System;
using System.Collections.Generic;
using HaloCreek.Models;
using HaloCreek.Services.PromptTemplates;
using HaloCreek.Services.SessionHistory;

namespace HaloCreek.ViewModels.Components
{
    public sealed class PromptTemplatePickerViewModel : ViewModelBase
    {
        private const string TemplatesGroupTitle = "Templates";
        private const string RecentInitialPromptsGroupTitle = "Recent Initial Prompts";

        private readonly IReadOnlyList<PromptTemplateItem> _templateItems;
        private readonly SessionHistoryStore _sessionHistoryStore;
        private PromptTemplateItem? _previewItem;

        public PromptTemplatePickerViewModel(
            IReadOnlyList<PromptTemplateItem> items,
            SessionHistoryStore sessionHistoryStore)
        {
            _templateItems = items ?? throw new ArgumentNullException(nameof(items));
            _sessionHistoryStore = sessionHistoryStore
                ?? throw new ArgumentNullException(nameof(sessionHistoryStore));
        }

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

        public IReadOnlyList<PromptTemplateItemGroup> GetGroupsSnapshot()
        {
            var groups = new List<PromptTemplateItemGroup>(2);

            if (_templateItems.Count > 0)
            {
                groups.Add(new PromptTemplateItemGroup(TemplatesGroupTitle, _templateItems));
            }

            var recentInitialPrompts =
                RecentInitialPromptTemplateItemBuilder.Build(_sessionHistoryStore.Sessions);
            if (recentInitialPrompts.Count > 0)
            {
                groups.Add(new PromptTemplateItemGroup(
                    RecentInitialPromptsGroupTitle,
                    recentInitialPrompts));
            }

            return groups;
        }

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
