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
        private readonly SessionHistoryQueryService _sessionHistoryQueryService;
        private IReadOnlyList<HistorySessionInfo> _loadedSessions = Array.Empty<HistorySessionInfo>();
        private IReadOnlyList<HistorySessionInfo> _sessions = Array.Empty<HistorySessionInfo>();
        private string _searchText = string.Empty;
        private HistorySessionInfo? _selectedSession;
        private Action<string>? _statusDispatcher;
        private string? _workspacePath;

        public HistorySessionsViewModel()
            : this(new SessionHistoryQueryService(
                new MockHistorySessionReader(),
                new ConfigService()))
        {
        }

        public HistorySessionsViewModel(SessionHistoryQueryService sessionHistoryQueryService)
        {
            _sessionHistoryQueryService = sessionHistoryQueryService;
            ResumeCommand = new RelayCommand<HistorySessionInfo>(ResumePlaceholder, HasSelectedSession);
            ReeditInitialPromptCommand = new RelayCommand<HistorySessionInfo>(ReeditInitialPromptPlaceholder, HasSelectedSession);
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

        public HistorySessionInfo? SelectedSession
        {
            get => _selectedSession;
            set
            {
                if (SetProperty(ref _selectedSession, value))
                {
                    ResumeCommand.NotifyCanExecuteChanged();
                    ReeditInitialPromptCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(SelectedSessionSummaryText));
                }
            }
        }

        public bool HasSessions => Sessions.Count > 0;

        public bool IsEmptyStateVisible => !HasSessions;

        public string SelectedSessionSummaryText => SelectedSession?.SessionSummaryText ?? string.Empty;

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
            var result = _sessionHistoryQueryService.GetSessions(WorkspacePath);
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

            if (SelectedSession is not null && !Sessions.Contains(SelectedSession))
            {
                SelectedSession = null;
            }
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

        private static bool HasSelectedSession(HistorySessionInfo? session)
        {
            return session is not null;
        }

        private static void ReeditInitialPromptPlaceholder(HistorySessionInfo? session)
        {
        }
    }
}
