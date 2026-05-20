using System;
using System.Collections.Generic;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class OngoingSessionsViewModel : ViewModelBase
    {
        private readonly SessionLifecycleService _sessionLifecycleService;
        private IReadOnlyList<OngoingSessionInfo> _sessions = Array.Empty<OngoingSessionInfo>();
        private string? _workspacePath;

        public OngoingSessionsViewModel(SessionLifecycleService sessionLifecycleService)
        {
            _sessionLifecycleService = sessionLifecycleService;
        }

        public string? WorkspacePath
        {
            get => _workspacePath;
            private set => SetProperty(ref _workspacePath, value);
        }

        public IReadOnlyList<OngoingSessionInfo> Sessions
        {
            get => _sessions;
            private set => SetProperty(ref _sessions, value);
        }

        public void SetWorkspacePath(string workspacePath)
        {
            WorkspacePath = workspacePath;
            RefreshSessions();
        }

        public void RefreshSessions()
        {
            Sessions = _sessionLifecycleService.GetOngoingSessions(WorkspacePath);
        }
    }
}
