using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class PromptEditorViewModel : ViewModelBase
    {
        private readonly WorkspaceRuntimeService _workspaceRuntimeService;
        private readonly SessionLifecycleService _sessionLifecycleService;
        private readonly TransientEventService _transientEventService;
        private IReadOnlyList<OngoingSessionInfo> _ongoingSessions = Array.Empty<OngoingSessionInfo>();
        private string _promptText = string.Empty;
        private OngoingSessionInfo? _selectedOngoingSession;
        private string? _workspacePath;

        public PromptEditorViewModel(
            SessionLifecycleService sessionLifecycleService,
            WorkspaceRuntimeService workspaceRuntimeService,
            TransientEventService transientEventService)
        {
            _sessionLifecycleService = sessionLifecycleService
                ?? throw new ArgumentNullException(nameof(sessionLifecycleService));
            _workspaceRuntimeService = workspaceRuntimeService
                ?? throw new ArgumentNullException(nameof(workspaceRuntimeService));
            _transientEventService = transientEventService
                ?? throw new ArgumentNullException(nameof(transientEventService));

            LaunchCommand = new RelayCommand(Launch);
            BringToFrontCommand = new RelayCommand<OngoingSessionInfo>(BringToFront, HasOngoingSession);
            ExitSessionCommand = new RelayCommand<OngoingSessionInfo>(ExitSession, HasOngoingSession);

            _sessionLifecycleService.SessionsChanged += HandleSessionsChanged;
            var workspacePath = _workspaceRuntimeService.CurrentWorkspacePath;
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                throw new InvalidOperationException("Workspace runtime is not initialized.");
            }

            ApplyWorkspacePath(workspacePath);
            _workspaceRuntimeService.WorkspaceChangedEvent += OnWorkspaceChanged;
        }

        public string PromptText
        {
            get => _promptText;
            set => SetProperty(ref _promptText, value ?? string.Empty);
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

        private void ApplyWorkspacePath(string workspacePath)
        {
            WorkspacePath = workspacePath;
            RefreshOngoingSessions();
        }

        public SessionLaunchResult LaunchPrompt()
        {
            if (string.IsNullOrWhiteSpace(PromptText))
            {
                return new SessionLaunchResult(false, "Prompt is empty.", null);
            }

            var config = _workspaceRuntimeService.EffectiveConfig;
            return _sessionLifecycleService.Launch(
                WorkspacePath!,
                PromptText,
                config.CodexExecutableName,
                config.CodexLaunchArguments);
        }

        private void Launch()
        {
            var result = LaunchPrompt();
            if (result.Started)
            {
                Log.Info("PromptEditor", result.StatusMessage);
                return;
            }

            _transientEventService.ReportUserActionFailure(
                "PromptEditor",
                "Launch failed",
                result.StatusMessage);
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

        private void HandleSessionsChanged(object? sender, EventArgs e)
        {
            // 走一下post防同步重入 现在已经不会从别的线程到这了
            Dispatcher.UIThread.Post(RefreshOngoingSessions);
        }

        private void OnWorkspaceChanged(object? sender, WorkspaceRuntimeChangedEventArgs e)
        {
            var previousWorkspacePath = WorkspacePath;
            ApplyWorkspacePath(e.WorkspacePath);
            ExitWorkspaceOngoingSessions(previousWorkspacePath);
        }

        private void ExitWorkspaceOngoingSessions(string? workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return;
            }

            foreach (var session in _sessionLifecycleService.GetOngoingSessions(workspacePath))
            {
                _sessionLifecycleService.Exit(session.Id);
            }
        }
    }
}
