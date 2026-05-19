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
        private WorkspaceInfo? _workspace;

        public OngoingSessionsViewModel()
            : this(new SessionLifecycleService())
        {
        }

        public OngoingSessionsViewModel(SessionLifecycleService sessionLifecycleService)
        {
            _sessionLifecycleService = sessionLifecycleService;
        }

        public WorkspaceInfo? Workspace
        {
            get => _workspace;
            private set => SetProperty(ref _workspace, value);
        }

        public IReadOnlyList<OngoingSessionInfo> Sessions
        {
            get => _sessions;
            private set => SetProperty(ref _sessions, value);
        }

        public void SetWorkspace(WorkspaceInfo workspace)
        {
            Workspace = workspace;
            RefreshSessions();
        }

        public void RefreshSessions()
        {
            Sessions = _sessionLifecycleService.GetOngoingSessions(Workspace);
        }
    }
}
