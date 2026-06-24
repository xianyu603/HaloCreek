using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services;
using HaloCreek.Services.Completions;
using HaloCreek.Services.SessionHistory;
using HaloCreek.ViewModels.Components;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class PromptEditorViewModel : ViewModelBase, IDisposable
    {
        private readonly SessionLifecycleService _sessionLifecycleService;
        private IReadOnlyList<OngoingSessionInfo> _ongoingSessions = Array.Empty<OngoingSessionInfo>();
        private OngoingSessionInfo? _selectedOngoingSession;
        private bool _isDisposed;

        public PromptEditorViewModel(
            SessionLifecycleService sessionLifecycleService,
            AppCommonRuntime appCommonRuntime,
            CompletionCoordinator completionCoordinator,
            SessionHistoryStore sessionHistoryStore)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);
            ArgumentNullException.ThrowIfNull(completionCoordinator);

            _sessionLifecycleService = sessionLifecycleService
                ?? throw new ArgumentNullException(nameof(sessionLifecycleService));
            PromptInput = new PromptInputViewModel(
                _sessionLifecycleService,
                appCommonRuntime,
                completionCoordinator,
                sessionHistoryStore);

            BringToFrontCommand = new RelayCommand<OngoingSessionInfo>(BringToFront, CanBringToFront);
            ExitSessionCommand = new RelayCommand<OngoingSessionInfo>(ExitSession, HasOngoingSession);

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

        public IRelayCommand<OngoingSessionInfo> ExitSessionCommand { get; }

        private void RefreshOngoingSessions()
        {
            var selectedSessionId = SelectedOngoingSession?.Id;
            OngoingSessions = _sessionLifecycleService.GetCurrentWorkspaceOngoingSessions();
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

        private void ExitSession(OngoingSessionInfo? session)
        {
            if (session is null)
            {
                return;
            }

            _sessionLifecycleService.Exit(session.Id);
            Log.Info("PromptEditor", "Session exit requested.");
        }

        private static bool HasOngoingSession(OngoingSessionInfo? session)
        {
            return session is not null;
        }

        private static bool CanBringToFront(OngoingSessionInfo? session)
        {
            return session is not null
                && session.State != OngoingSessionState.Launching;
        }

        private void HandleSessionsChanged(object? sender, EventArgs e)
        {
            // 走一下post防同步重入 现在已经不会从别的线程到这了
            Dispatcher.UIThread.Post(RefreshOngoingSessions);
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
