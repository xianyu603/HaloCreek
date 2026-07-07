using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services;
using HaloCreek.Services.Completions;
using HaloCreek.Services.SessionHistory;
using HaloCreek.Services.WorkspaceSnapshots;
using HaloCreek.ViewModels.Components;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class PromptEditorViewModel : ViewModelBase, IDisposable
    {
        private readonly SessionLifecycleService _sessionLifecycleService;
        private readonly TmuxService _tmuxService;
        private readonly TerminalService _terminalService;
        private IReadOnlyList<OngoingSessionInfo> _ongoingSessions = Array.Empty<OngoingSessionInfo>();
        private OngoingSessionInfo? _selectedOngoingSession;
        private bool _isDisposed;

        public PromptEditorViewModel(
            SessionLifecycleService sessionLifecycleService,
            TmuxService tmuxService,
            TerminalService terminalService,
            AppCommonRuntime appCommonRuntime,
            CompletionCoordinator completionCoordinator,
            IWorkspaceSnapshotSource<SessionHistorySnapshot> historySnapshots)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);
            ArgumentNullException.ThrowIfNull(completionCoordinator);

            _sessionLifecycleService = sessionLifecycleService
                ?? throw new ArgumentNullException(nameof(sessionLifecycleService));
            _tmuxService = tmuxService ?? throw new ArgumentNullException(nameof(tmuxService));
            _terminalService = terminalService ?? throw new ArgumentNullException(nameof(terminalService));
            PromptInput = new PromptInputViewModel(
                _sessionLifecycleService,
                appCommonRuntime,
                completionCoordinator,
                historySnapshots);

            BringToFrontCommand = new RelayCommand<OngoingSessionInfo>(BringToFront, CanBringToFront);
            OpenCliCommand = new AsyncRelayCommand<OngoingSessionInfo>(OpenCliAsync, CanOpenCli);
            ExitSessionCommand = new RelayCommand<OngoingSessionInfo>(ExitSession, CanExitSession);

            _sessionLifecycleService.SessionsChanged += HandleSessionsChanged;
            RefreshOngoingSessions();
        }

        public PromptInputViewModel PromptInput { get; }

        public IReadOnlyList<OngoingSessionInfo> OngoingSessions
        {
            get => _ongoingSessions;
            private set
            {
                if (SetProperty(ref _ongoingSessions, value))
                {
                    OnPropertyChanged(nameof(HasOngoingSessions));
                    OnPropertyChanged(nameof(IsOngoingSessionsEmpty));
                    BringToFrontCommand.NotifyCanExecuteChanged();
                    OpenCliCommand.NotifyCanExecuteChanged();
                    ExitSessionCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public OngoingSessionInfo? SelectedOngoingSession
        {
            get => _selectedOngoingSession;
            set => SetProperty(ref _selectedOngoingSession, value);
        }

        public bool HasOngoingSessions => OngoingSessions.Count > 0;

        public bool IsOngoingSessionsEmpty => !HasOngoingSessions;

        public IRelayCommand<OngoingSessionInfo> BringToFrontCommand { get; }

        public IAsyncRelayCommand<OngoingSessionInfo> OpenCliCommand { get; }

        public IRelayCommand<OngoingSessionInfo> ExitSessionCommand { get; }

        private void RefreshOngoingSessions()
        {
            var selectedSessionId = SelectedOngoingSession?.Id;
            OngoingSessions = _sessionLifecycleService.GetOngoingSessionInfos();
            SelectedOngoingSession = OngoingSessions.FirstOrDefault(
                session => string.Equals(session.Id, selectedSessionId, StringComparison.Ordinal));
        }

        private void BringToFront(OngoingSessionInfo? session)
        {
            if (session is null)
            {
                return;
            }

            _sessionLifecycleService.BringToFront(session.Id);
            Log.Info("PromptEditor", "Bring to front requested.");
        }

        private async Task OpenCliAsync(OngoingSessionInfo? session)
        {
            if (session is null)
            {
                return;
            }

            Log.Info("PromptEditor", "CLI entry requested.");

            await Task.Run(() =>
            {
                _terminalService.LaunchFrontClient(
                    _tmuxService.GetFrontClientStartupCommand(session.Id));
            });

            Log.Info("PromptEditor", "CLI entry completed.");
        }

        private void ExitSession(OngoingSessionInfo? session)
        {
            if (session is null)
            {
                return;
            }

            _sessionLifecycleService.Exit(session.Id);
            Log.Info("PromptEditor", "Session exit requested.");
        }

        private static bool CanExitSession(OngoingSessionInfo? session)
        {
            return session is not null
                && session.IsInteractive;
        }

        private static bool CanBringToFront(OngoingSessionInfo? session)
        {
            return session is not null
                && session.IsInteractive;
        }

        private static bool CanOpenCli(OngoingSessionInfo? session)
        {
            return session is not null
                && session.IsInteractive;
        }

        private void HandleSessionsChanged(object? sender, SessionLifecycleChangedEventArgs e)
        {
            // 走一下post防同步重入 现在已经不会从别的线程到这了
            Dispatcher.UIThread.Post(() => ApplySessionChange(e));
        }

        private void ApplySessionChange(SessionLifecycleChangedEventArgs e)
        {
            var selectedSessionId = SelectedOngoingSession?.Id;
            var sessions = OngoingSessions.ToList();

            if (e.PreviousSession is not null)
            {
                UpsertSession(sessions, e.PreviousSession);
            }

            if (e.Session is null)
            {
                RefreshOngoingSessions();
                return;
            }

            if (e.ChangeKind == SessionLifecycleChangeKind.Removed)
            {
                sessions.RemoveAll(session => string.Equals(
                    session.Id,
                    e.Session.Id,
                    StringComparison.Ordinal));
            }
            else
            {
                UpsertSession(sessions, e.Session);
            }

            OngoingSessions = sessions
                .OrderBy(session => session.StartedAt)
                .ToArray();
            SelectedOngoingSession = OngoingSessions.FirstOrDefault(
                session => string.Equals(session.Id, selectedSessionId, StringComparison.Ordinal));
        }

        private static void UpsertSession(
            List<OngoingSessionInfo> sessions,
            OngoingSessionInfo session)
        {
            var index = sessions.FindIndex(candidate => string.Equals(
                candidate.Id,
                session.Id,
                StringComparison.Ordinal));
            if (index < 0)
            {
                sessions.Add(session);
                return;
            }

            sessions[index] = session;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _sessionLifecycleService.SessionsChanged -= HandleSessionsChanged;
            PromptInput.Dispose();
        }
    }
}
