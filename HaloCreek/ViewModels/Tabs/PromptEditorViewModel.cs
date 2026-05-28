using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class PromptEditorViewModel : ViewModelBase
    {
        private readonly ConfigService _configService;
        private readonly SessionLifecycleService _sessionLifecycleService;
        private readonly ApplicationStatusService _applicationStatusService;
        private readonly TransientEventService _transientEventService;
        private IReadOnlyList<OngoingSessionInfo> _ongoingSessions = Array.Empty<OngoingSessionInfo>();
        private string _promptText = string.Empty;
        private OngoingSessionInfo? _selectedOngoingSession;
        private Action<string>? _statusDispatcher;
        private string? _workspacePath;

        public PromptEditorViewModel(
            SessionLifecycleService sessionLifecycleService,
            ConfigService configService,
            ApplicationStatusService applicationStatusService,
            TransientEventService transientEventService)
        {
            _sessionLifecycleService = sessionLifecycleService;
            _configService = configService;
            _applicationStatusService = applicationStatusService
                ?? throw new ArgumentNullException(nameof(applicationStatusService));
            _transientEventService = transientEventService
                ?? throw new ArgumentNullException(nameof(transientEventService));

            LaunchCommand = new RelayCommand(Launch, CanLaunchPrompt);
            BringToFrontCommand = new RelayCommand<OngoingSessionInfo>(BringToFront, HasOngoingSession);
            ExitSessionCommand = new RelayCommand<OngoingSessionInfo>(ExitSession, HasOngoingSession);

            _sessionLifecycleService.SessionsChanged += HandleSessionsChanged;
        }

        public string PromptText
        {
            get => _promptText;
            set
            {
                if (SetProperty(ref _promptText, value ?? string.Empty))
                {
                    LaunchCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string? WorkspacePath
        {
            get => _workspacePath;
            private set => SetProperty(ref _workspacePath, value);
        }

        public IReadOnlyList<OngoingSessionInfo> OngoingSessions
        {
            get => _ongoingSessions;
            private set
            {
                if (SetProperty(ref _ongoingSessions, value))
                {
                    OnPropertyChanged(nameof(HasOngoingSessions));
                    OnPropertyChanged(nameof(IsOngoingSessionsEmpty));
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

        public IRelayCommand LaunchCommand { get; }

        public IRelayCommand<OngoingSessionInfo> BringToFrontCommand { get; }

        public IRelayCommand<OngoingSessionInfo> ExitSessionCommand { get; }

        public void SetWorkspacePath(string workspacePath)
        {
            WorkspacePath = workspacePath;
            RefreshOngoingSessions();
            LaunchCommand.NotifyCanExecuteChanged();
        }

        public SessionLaunchResult LaunchPrompt()
        {
            if (string.IsNullOrWhiteSpace(WorkspacePath))
            {
                return new SessionLaunchResult(false, "No workspace selected.", null);
            }

            if (string.IsNullOrWhiteSpace(PromptText))
            {
                return new SessionLaunchResult(false, "Prompt is empty.", null);
            }

            var config = _configService.LoadEffectiveConfig(WorkspacePath!);
            return _sessionLifecycleService.Launch(WorkspacePath!, PromptText, config);
        }

        private bool CanLaunchPrompt()
        {
            return !string.IsNullOrWhiteSpace(WorkspacePath)
                && !string.IsNullOrWhiteSpace(PromptText);
        }

        private void Launch()
        {
            var result = LaunchPrompt();
            _statusDispatcher?.Invoke(result.StatusMessage);
        }

        private void RefreshOngoingSessions()
        {
            var selectedSessionId = SelectedOngoingSession?.Id;
            OngoingSessions = _sessionLifecycleService.GetOngoingSessions(WorkspacePath);
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
            _statusDispatcher?.Invoke("Bring to front requested.");
        }

        private void ExitSession(OngoingSessionInfo? session)
        {
            if (session is null)
            {
                return;
            }

            _sessionLifecycleService.Exit(session.Id);
            _statusDispatcher?.Invoke("Session exit requested.");
        }

        private static bool HasOngoingSession(OngoingSessionInfo? session)
        {
            return session is not null;
        }

        private void HandleSessionsChanged(object? sender, EventArgs e)
        {
            // 走一下post防同步重入 现在已经不会从别的线程到这了
            Dispatcher.UIThread.Post(RefreshOngoingSessions);
        }
    }
}
