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
        private readonly SessionHistoryRefreshService _sessionHistoryRefreshService;
        private readonly SessionLifecycleService? _sessionLifecycleService;
        private readonly ConfigService? _configService;
        private IReadOnlyList<HistorySessionInfo> _loadedSessions = Array.Empty<HistorySessionInfo>();
        private IReadOnlyList<HistorySessionInfo> _sessions = Array.Empty<HistorySessionInfo>();
        private string _searchText = string.Empty;
        private HistorySessionInfo? _selectedSession;
        private Action<HistorySessionInfo>? _reeditInitialPromptDispatcher;
        private Action<string>? _statusDispatcher;
        private string? _workspacePath;

        public HistorySessionsViewModel()
            : this(new SessionHistoryRefreshService(
                new MockHistorySessionReader(),
                new ConfigService()))
        {
        }

        public HistorySessionsViewModel(
            SessionHistoryRefreshService sessionHistoryRefreshService,
            SessionLifecycleService? sessionLifecycleService = null,
            ConfigService? configService = null)
        {
            _sessionHistoryRefreshService = sessionHistoryRefreshService;
            _sessionLifecycleService = sessionLifecycleService;
            _configService = configService;
            _sessionHistoryRefreshService.SetRefreshCompletedHandler(HandleRefreshCompleted);
            ResumeCommand = new RelayCommand<HistorySessionInfo>(Resume, HasSelectedSession);
            ReeditInitialPromptCommand = new RelayCommand<HistorySessionInfo>(ReeditInitialPrompt, HasSelectedSession);
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
            _loadedSessions = Array.Empty<HistorySessionInfo>();
            ApplySearch();
            _sessionHistoryRefreshService.SetWorkspacePath(workspacePath);
        }

        public void SetStatusDispatcher(Action<string> statusDispatcher)
        {
            _statusDispatcher = statusDispatcher ?? throw new ArgumentNullException(nameof(statusDispatcher));
        }

        public void SetReeditInitialPromptDispatcher(Action<HistorySessionInfo> reeditInitialPromptDispatcher)
        {
            _reeditInitialPromptDispatcher = reeditInitialPromptDispatcher
                ?? throw new ArgumentNullException(nameof(reeditInitialPromptDispatcher));
        }

        private void ApplySearch()
        {
            Sessions = FilterSessions(_loadedSessions, SearchText);

            if (SelectedSession is null)
            {
                return;
            }

            var refreshedSelection = Sessions.FirstOrDefault(
                session => string.Equals(session.Id, SelectedSession.Id, StringComparison.Ordinal));

            SelectedSession = refreshedSelection;
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

        private void HandleRefreshCompleted(SessionHistoryRefreshResult refreshResult)
        {
            if (!string.Equals(WorkspacePath, refreshResult.WorkspacePath, StringComparison.Ordinal))
            {
                return;
            }

            if (refreshResult.HistoryResult is null)
            {
                if (!string.IsNullOrWhiteSpace(refreshResult.ErrorMessage))
                {
                    _statusDispatcher?.Invoke($"Failed to load history sessions: {refreshResult.ErrorMessage}");
                }

                return;
            }

            _loadedSessions = refreshResult.HistoryResult.Sessions;
            ApplySearch();

            if (refreshResult.HistoryResult.SkippedFileCount > 0)
            {
                _statusDispatcher?.Invoke(
                    $"Loaded {_loadedSessions.Count} sessions, skipped {refreshResult.HistoryResult.SkippedFileCount} invalid files.");
            }
        }

        private void Resume(HistorySessionInfo? session)
        {
            if (session is null)
            {
                return;
            }

            if (_sessionLifecycleService is null || _configService is null)
            {
                _statusDispatcher?.Invoke("Resume is not available.");
                return;
            }

            var config = _configService.LoadEffectiveConfig(WorkspacePath);
            var result = _sessionLifecycleService.Resume(session, WorkspacePath ?? string.Empty, config);
            _statusDispatcher?.Invoke(result.StatusMessage);
        }

        private static bool HasSelectedSession(HistorySessionInfo? session)
        {
            return session is not null;
        }

        private void ReeditInitialPrompt(HistorySessionInfo? session)
        {
            if (session is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(session.InitialPrompt))
            {
                _statusDispatcher?.Invoke("Initial prompt is empty.");
                return;
            }

            if (_reeditInitialPromptDispatcher is null)
            {
                _statusDispatcher?.Invoke("Prompt editor is not available.");
                return;
            }

            try
            {
                _reeditInitialPromptDispatcher.Invoke(session);
            }
            catch (InvalidOperationException ex)
            {
                _statusDispatcher?.Invoke($"Failed to reedit initial prompt: {ex.Message}");
            }
        }
    }
}
