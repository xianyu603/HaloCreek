using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Models;
using HaloCreek.Services;
using HaloCreek.Services.SessionHistory;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class HistorySessionsViewModel : ViewModelBase
    {
        private readonly SessionHistoryService _sessionHistoryService;
        private IReadOnlyList<HistorySessionInfo> _sessions = Array.Empty<HistorySessionInfo>();
        private string _searchText = string.Empty;
        private string? _workspacePath;

        public HistorySessionsViewModel()
            : this(new SessionHistoryService(
                new MockHistorySessionReader(),
                new ConfigService()))
        {
        }

        public HistorySessionsViewModel(SessionHistoryService sessionHistoryService)
        {
            _sessionHistoryService = sessionHistoryService;
            ResumeCommand = new RelayCommand<HistorySessionInfo>(ResumePlaceholder);
            ReeditInitialPromptCommand = new RelayCommand<HistorySessionInfo>(ReeditInitialPromptPlaceholder);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    RefreshSessions();
                }
            }
        }

        public string? WorkspacePath
        {
            get => _workspacePath;
            private set => SetProperty(ref _workspacePath, value);
        }

        public IReadOnlyList<HistorySessionInfo> Sessions
        {
            get => _sessions;
            private set
            {
                if (SetProperty(ref _sessions, value))
                {
                    OnPropertyChanged(nameof(HasSessions));
                    OnPropertyChanged(nameof(IsEmptyStateVisible));
                }
            }
        }

        public bool HasSessions => Sessions.Count > 0;

        public bool IsEmptyStateVisible => !HasSessions;

        public IRelayCommand<HistorySessionInfo> ResumeCommand { get; }

        public IRelayCommand<HistorySessionInfo> ReeditInitialPromptCommand { get; }

        public void SetWorkspacePath(string workspacePath)
        {
            WorkspacePath = workspacePath;
            RefreshSessions();
        }

        private void RefreshSessions()
        {
            Sessions = _sessionHistoryService.GetFilteredSessions(WorkspacePath, SearchText);
        }

        private static void ResumePlaceholder(HistorySessionInfo? session)
        {
        }

        private static void ReeditInitialPromptPlaceholder(HistorySessionInfo? session)
        {
        }
    }
}
