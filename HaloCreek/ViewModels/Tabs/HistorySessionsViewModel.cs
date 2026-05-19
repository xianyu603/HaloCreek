using System;
using System.Collections.Generic;
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
        private WorkspaceInfo? _workspace;

        public HistorySessionsViewModel()
            : this(new SessionHistoryService(
                new MockHistorySessionReader(),
                new ConfigService()))
        {
        }

        public HistorySessionsViewModel(SessionHistoryService sessionHistoryService)
        {
            _sessionHistoryService = sessionHistoryService;
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

        public WorkspaceInfo? Workspace
        {
            get => _workspace;
            private set => SetProperty(ref _workspace, value);
        }

        public IReadOnlyList<HistorySessionInfo> Sessions
        {
            get => _sessions;
            private set => SetProperty(ref _sessions, value);
        }

        public void SetWorkspace(WorkspaceInfo workspace)
        {
            Workspace = workspace;
            RefreshSessions();
        }

        private void RefreshSessions()
        {
            Sessions = _sessionHistoryService.SearchSessions(Workspace, SearchText);
        }
    }
}
