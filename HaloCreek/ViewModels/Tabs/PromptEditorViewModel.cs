using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class PromptEditorViewModel : ViewModelBase
    {
        private readonly SessionLifecycleService _sessionLifecycleService;
        private readonly TransientEventService _transientEventService;
        private IReadOnlyList<OngoingSessionInfo> _ongoingSessions = Array.Empty<OngoingSessionInfo>();
        private string _promptText = string.Empty;
        private OngoingSessionInfo? _selectedOngoingSession;

        public PromptEditorViewModel(
            SessionLifecycleService sessionLifecycleService,
            AppCommonRuntime appCommonRuntime)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _sessionLifecycleService = sessionLifecycleService
                ?? throw new ArgumentNullException(nameof(sessionLifecycleService));
            _transientEventService = appCommonRuntime.TransientEventService;

            LaunchCommand = new AsyncRelayCommand(LaunchAsync, CanLaunchPrompt);
            SendToFrontCommand = new RelayCommand(SendToFront, CanLaunchPrompt);
            BringToFrontCommand = new RelayCommand<OngoingSessionInfo>(BringToFront, CanBringToFront);
            ExitSessionCommand = new RelayCommand<OngoingSessionInfo>(ExitSession, HasOngoingSession);

            // Prompt editor 与 SessionLifecycleService 同应用生命周期，当前不做显式退订。
            _sessionLifecycleService.SessionsChanged += HandleSessionsChanged;
            RefreshOngoingSessions();
        }

        public string PromptText
        {
            get => _promptText;
            set
            {
                if (SetProperty(ref _promptText, value ?? string.Empty))
                {
                    LaunchCommand.NotifyCanExecuteChanged();
                    SendToFrontCommand.NotifyCanExecuteChanged();
                }
            }
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

        public IAsyncRelayCommand LaunchCommand { get; }

        public IRelayCommand SendToFrontCommand { get; }

        public IRelayCommand<OngoingSessionInfo> BringToFrontCommand { get; }

        public IRelayCommand<OngoingSessionInfo> ExitSessionCommand { get; }

        private async Task LaunchAsync()
        {
            try
            {
                var session = await _sessionLifecycleService.LaunchAsync(PromptText);
                Log.Info("PromptEditor", $"Codex session launch requested: {session.Id}");
            }
            catch (InvalidOperationException ex)
            {
                _transientEventService.ReportUserActionFailure(
                    "PromptEditor",
                    "Launch failed",
                    ex.Message);
            }
        }

        private void SendToFront()
        {
            _sessionLifecycleService.SendMessageToFrontSession(PromptText);
        }

        private bool CanLaunchPrompt()
        {
            return !string.IsNullOrWhiteSpace(PromptText);
        }

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
    }
}
