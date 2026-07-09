using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
        private readonly TransientEventService _transientEventService;
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
            _transientEventService = appCommonRuntime.TransientEventService;
            PromptInput = new PromptInputViewModel(
                _sessionLifecycleService,
                appCommonRuntime,
                completionCoordinator,
                historySnapshots);

            BringToFrontCommand = new RelayCommand<OngoingSession>(BringToFront, CanBringToFront);
            OpenCliCommand = new AsyncRelayCommand<OngoingSession>(OpenCliAsync);
            RestartSessionCommand = new AsyncRelayCommand<OngoingSession>(RestartSessionAsync);
            ExitSessionCommand = new RelayCommand<OngoingSession>(ExitSession, CanExitSession);

            ((INotifyCollectionChanged)_sessionLifecycleService.AllSessions).CollectionChanged +=
                OngoingSessions_OnCollectionChanged;
        }

        public PromptInputViewModel PromptInput { get; }

        public ReadOnlyObservableCollection<OngoingSession> OngoingSessions =>
            _sessionLifecycleService.AllSessions;

        public bool HasOngoingSessions => OngoingSessions.Count > 0;

        public bool IsOngoingSessionsEmpty => !HasOngoingSessions;

        public IRelayCommand<OngoingSession> BringToFrontCommand { get; }

        public IAsyncRelayCommand<OngoingSession> OpenCliCommand { get; }

        public IAsyncRelayCommand<OngoingSession> RestartSessionCommand { get; }

        public IRelayCommand<OngoingSession> ExitSessionCommand { get; }

        private void BringToFront(OngoingSession? session)
        {
            if (session is null)
            {
                return;
            }

            _sessionLifecycleService.BringToFront(session.Id);
            Log.Info("PromptEditor", "Bring to front requested.");
        }

        private async Task OpenCliAsync(OngoingSession? session)
        {
            if (session is null || !session.CanOpenCli)
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

        private void ExitSession(OngoingSession? session)
        {
            if (session is null)
            {
                return;
            }

            _sessionLifecycleService.Exit(session.Id);
            Log.Info("PromptEditor", "Session exit requested.");
        }

        private async Task RestartSessionAsync(OngoingSession? session)
        {
            if (session is null || !session.CanRestart)
            {
                return;
            }

            try
            {
                var restartMode = session.RestartSource switch
                {
                    SessionRestartSource.CodexSession source => $"resume codex session {source.SessionId}",
                    SessionRestartSource.LaunchPrompt => "launch from first prompt",
                    _ => "unknown restart source",
                };

                var restartedSession = await _sessionLifecycleService.RestartAsync(session);
                Log.Info(
                    "PromptEditor",
                    $"Session restart requested. SessionId={session.Id}, NewSessionId={restartedSession.Id}, Action={restartMode}.");
            }
            catch (InvalidOperationException ex)
            {
                _transientEventService.ReportUserActionFailure(
                    "PromptEditor",
                    "Restart failed",
                    ex.Message,
                    ex);
            }
        }

        private static bool CanExitSession(OngoingSession? session)
        {
            return session is not null;
        }

        private static bool CanBringToFront(OngoingSession? session)
        {
            return session is not null;
        }

        private void OngoingSessions_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // 走一下post防同步重入 现在已经不会从别的线程到这了
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OngoingSessions_OnCollectionChanged(sender, e));
                return;
            }

            if (_isDisposed)
            {
                return;
            }

            OnPropertyChanged(nameof(HasOngoingSessions));
            OnPropertyChanged(nameof(IsOngoingSessionsEmpty));
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            ((INotifyCollectionChanged)_sessionLifecycleService.AllSessions).CollectionChanged -=
                OngoingSessions_OnCollectionChanged;

            PromptInput.Dispose();
        }
    }
}
