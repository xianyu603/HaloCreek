using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services;
using HaloCreek.Services.SessionHistory;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class HistorySessionsViewModel : ViewModelBase, IDisposable
    {
        private readonly IWorkspaceSnapshotSource<SessionHistorySnapshot> _historySnapshots;
        private readonly SessionLifecycleService _sessionLifecycleService;
        private readonly TransientEventService _transientEventService;
        private IReadOnlyList<HistorySessionInfo> _sessions = Array.Empty<HistorySessionInfo>();
        private string _searchText = string.Empty;
        private HistorySessionInfo? _selectedSession;
        private bool _isDisposed;

        public HistorySessionsViewModel(
            IWorkspaceSnapshotSource<SessionHistorySnapshot> historySnapshots,
            SessionLifecycleService sessionLifecycleService,
            AppCommonRuntime appCommonRuntime)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _historySnapshots = historySnapshots
                ?? throw new ArgumentNullException(nameof(historySnapshots));
            _sessionLifecycleService = sessionLifecycleService
                ?? throw new ArgumentNullException(nameof(sessionLifecycleService));
            _transientEventService = appCommonRuntime.TransientEventService;
            ResumeCommand = new AsyncRelayCommand<HistorySessionInfo>(ResumeAsync, HasSelectedSession);
            _historySnapshots.Changed += HandleHistoryChanged;
            ApplySearch();
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

        public IReadOnlyList<HistorySessionInfo> Sessions
        {
            get => _sessions;
            private set => SetProperty(ref _sessions, value);
        }

        public HistorySessionInfo? SelectedSession
        {
            get => _selectedSession;
            set
            {
                if (SetProperty(ref _selectedSession, value))
                {
                    ResumeCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(SelectedSessionSummaryText));
                }
            }
        }

        public string SelectedSessionSummaryText => SelectedSession?.SessionSummaryText ?? string.Empty;

        public IAsyncRelayCommand<HistorySessionInfo> ResumeCommand { get; }

        private void ApplySearch()
        {
            var filteredSessions = FilterSessions(_historySnapshots.Current.Sessions, SearchText);
            var previousSelection = SelectedSession;

            if (!AreVisibleSessionListsEquivalent(Sessions, filteredSessions))
            {
                Sessions = filteredSessions;
            }

            if (previousSelection is null)
            {
                return;
            }

            var refreshedSelection = filteredSessions.FirstOrDefault(
                session => string.Equals(session.Id, previousSelection.Id, StringComparison.Ordinal));

            if (refreshedSelection is null)
            {
                SelectedSession = null;
                return;
            }

            if (!AreSelectedSessionDetailsEquivalent(previousSelection, refreshedSelection))
            {
                SelectedSession = refreshedSelection;
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

        private static bool AreVisibleSessionListsEquivalent(
            IReadOnlyList<HistorySessionInfo> currentSessions,
            IReadOnlyList<HistorySessionInfo> refreshedSessions)
        {
            if (currentSessions.Count != refreshedSessions.Count)
            {
                return false;
            }

            for (var index = 0; index < currentSessions.Count; index++)
            {
                var currentSession = currentSessions[index];
                var refreshedSession = refreshedSessions[index];

                if (!string.Equals(currentSession.Id, refreshedSession.Id, StringComparison.Ordinal) ||
                    !AreVisibleSessionFieldsEquivalent(currentSession, refreshedSession))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreVisibleSessionFieldsEquivalent(
            HistorySessionInfo currentSession,
            HistorySessionInfo refreshedSession)
        {
            return string.Equals(currentSession.CreatedAtLocalText, refreshedSession.CreatedAtLocalText, StringComparison.Ordinal) &&
                string.Equals(currentSession.LastUpdatedAtLocalText, refreshedSession.LastUpdatedAtLocalText, StringComparison.Ordinal) &&
                string.Equals(currentSession.InitialPromptText, refreshedSession.InitialPromptText, StringComparison.Ordinal) &&
                string.Equals(currentSession.LatestActivityText, refreshedSession.LatestActivityText, StringComparison.Ordinal);
        }

        private static bool AreSelectedSessionDetailsEquivalent(
            HistorySessionInfo currentSession,
            HistorySessionInfo refreshedSession)
        {
            return string.Equals(currentSession.SessionSummaryText, refreshedSession.SessionSummaryText, StringComparison.Ordinal);
        }

        private void HandleHistoryChanged(object? sender, EventArgs e)
        {
            var snapshot = _historySnapshots.Current;

            ApplySearch();

            if (snapshot.SkippedFileCount > 0)
            {
                Log.Warning(
                    "HistorySessions",
                    $"Loaded {snapshot.Sessions.Count} sessions, skipped {snapshot.SkippedFileCount} invalid files.");
            }
        }

        private async Task ResumeAsync(HistorySessionInfo? session)
        {
            if (session is null)
            {
                return;
            }

            try
            {
                var resumedSession = await _sessionLifecycleService.ResumeAsync(session);
                Log.Info("HistorySessions", $"Codex session resume requested: {resumedSession.Id}");
            }
            catch (InvalidOperationException ex)
            {
                _transientEventService.ReportUserActionFailure(
                    "HistorySessions",
                    "Resume failed",
                    ex.Message);
            }
        }

        private static bool HasSelectedSession(HistorySessionInfo? session)
        {
            return session is not null;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _historySnapshots.Changed -= HandleHistoryChanged;
        }
    }
}
