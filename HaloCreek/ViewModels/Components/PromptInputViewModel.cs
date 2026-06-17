using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Logging;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Components
{
    public sealed class PromptInputViewModel : ViewModelBase, IDisposable
    {
        private const string LogCategory = "PromptInput";

        public const string DefaultPlaceholderText =
            "Type a prompt or drop files here. Ctrl+Enter launches, Alt+Enter sends to front.";

        private readonly SessionLifecycleService _sessionLifecycleService;
        private readonly TransientEventService _transientEventService;
        private string _promptText = string.Empty;
        private bool _hasFrontSession;
        private bool _isDisposed;

        public PromptInputViewModel(
            SessionLifecycleService sessionLifecycleService,
            AppCommonRuntime appCommonRuntime)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _sessionLifecycleService = sessionLifecycleService
                ?? throw new ArgumentNullException(nameof(sessionLifecycleService));
            _transientEventService = appCommonRuntime.TransientEventService;

            LaunchCommand = new AsyncRelayCommand(LaunchAsync, HasPromptText);
            SendToFrontCommand = new RelayCommand(SendToFront, CanSendToFront);

            _sessionLifecycleService.SessionsChanged += RefreshFrontSessionState;
            RefreshFrontSessionState();
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

        public string PlaceholderText => DefaultPlaceholderText;

        public IAsyncRelayCommand LaunchCommand { get; }

        public IRelayCommand SendToFrontCommand { get; }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _sessionLifecycleService.SessionsChanged -= RefreshFrontSessionState;
        }

        private async Task LaunchAsync()
        {
            try
            {
                var session = await _sessionLifecycleService.LaunchAsync(PromptText);
                Log.Info(LogCategory, $"Codex session launch requested: {session.Id}");
            }
            catch (InvalidOperationException ex)
            {
                _transientEventService.ReportUserActionFailure(
                    LogCategory,
                    "Launch failed",
                    ex.Message);
            }
        }

        private void SendToFront()
        {
            _sessionLifecycleService.SendMessageToFrontSession(PromptText);
        }

        private bool HasPromptText()
        {
            return !string.IsNullOrWhiteSpace(PromptText);
        }

        private bool CanSendToFront()
        {
            return _hasFrontSession && HasPromptText();
        }

        private void RefreshFrontSessionState(object? sender = null, EventArgs? e = null)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => RefreshFrontSessionState());
                return;
            }

            if (_isDisposed)
            {
                return;
            }

            var hasFrontSession = _sessionLifecycleService.HasFrontSession;
            if (_hasFrontSession != hasFrontSession)
            {
                _hasFrontSession = hasFrontSession;
                SendToFrontCommand.NotifyCanExecuteChanged();
            }
        }
    }
}
