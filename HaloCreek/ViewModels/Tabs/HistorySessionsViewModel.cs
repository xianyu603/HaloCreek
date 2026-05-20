using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Models;
using HaloCreek.Services;
using HaloCreek.Services.SessionHistory;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class HistorySessionsViewModel : ViewModelBase
    {
        private readonly SessionHistoryService _sessionHistoryService;
        private IReadOnlyList<HistorySessionInfo> _loadedSessions = Array.Empty<HistorySessionInfo>();
        private IReadOnlyList<HistorySessionInfo> _sessions = Array.Empty<HistorySessionInfo>();
        private string _searchText = string.Empty;
        private Action<string>? _statusDispatcher;
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
                    ApplySearch();
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
            LoadSessions();
        }

        public void SetStatusDispatcher(Action<string> statusDispatcher)
        {
            _statusDispatcher = statusDispatcher ?? throw new ArgumentNullException(nameof(statusDispatcher));
        }

        private void LoadSessions()
        {
            var result = _sessionHistoryService.GetSessions(WorkspacePath);
            _loadedSessions = result.Sessions;
            ApplySearch();

            if (result.SkippedFileCount > 0)
            {
                _statusDispatcher?.Invoke(
                    $"Loaded {_loadedSessions.Count} sessions, skipped {result.SkippedFileCount} invalid files.");
            }
        }

        private void ApplySearch()
        {
            Sessions = FilterSessions(_loadedSessions, SearchText);
        }

        private static IReadOnlyList<HistorySessionInfo> FilterSessions(
            IReadOnlyList<HistorySessionInfo> sessions,
            string? searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return sessions;
            }

            var query = searchText.Trim();

            return sessions
                .Where(session =>
                    session.InitialPrompt.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        private static void ResumePlaceholder(HistorySessionInfo? session)
        {
        }

        private static void ReeditInitialPromptPlaceholder(HistorySessionInfo? session)
        {
        }
    }
}
