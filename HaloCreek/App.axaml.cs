using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HaloCreek.Infrastructure;
using HaloCreek.Services;
using HaloCreek.Services.SessionHistory;
using HaloCreek.ViewModels;
using HaloCreek.ViewModels.Components;
using HaloCreek.ViewModels.Tabs;
using HaloCreek.Views;

namespace HaloCreek
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow();
                var appServices = CreateAppServices(mainWindow);
                mainWindow.DataContext = appServices.MainWindowViewModel;
                desktop.Exit += (_, _) => appServices.Dispose();
                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static AppServices CreateAppServices(MainWindow mainWindow)
        {
            var platformInfrastructure = new PlatformInfrastructure(mainWindow);
            var applicationStatusService = new ApplicationStatusService();
            var transientEventService = new TransientEventService(platformInfrastructure);
            var appCommonRuntime = new AppCommonRuntime(
                platformInfrastructure,
                applicationStatusService,
                transientEventService);
            var workspaceCacheService = new WorkspaceCacheService();
            var configService = new ConfigService();
            var workspaceRuntimeService = new WorkspaceRuntimeService(
                appCommonRuntime,
                workspaceCacheService,
                configService);
            workspaceRuntimeService.InitializeStartupWorkspace();
            var tmuxService = new TmuxService(appCommonRuntime);
            var terminalService = new TerminalService(appCommonRuntime);
            var sessionLifecycleService = new SessionLifecycleService(tmuxService, terminalService);
            var gitService = new GitService();

            ISessionHistoryReader sessionHistoryReader = new CodexSessionHistoryReader(appCommonRuntime);
            var sessionHistoryQueryService = new SessionHistoryQueryService(sessionHistoryReader);
            var sessionHistoryRefreshService = new SessionHistoryRefreshService(sessionHistoryQueryService);

            var promptEditor = new PromptEditorViewModel(
                sessionLifecycleService,
                workspaceRuntimeService,
                appCommonRuntime);
            var historySessions = new HistorySessionsViewModel(
                sessionHistoryRefreshService,
                sessionLifecycleService,
                workspaceRuntimeService,
                appCommonRuntime);
            var git = new GitViewModel(
                gitService,
                workspaceRuntimeService,
                appCommonRuntime);
            var workspaceFooter = new WorkspaceFooterViewModel(
                workspaceRuntimeService,
                appCommonRuntime);

            var mainWindowViewModel = new MainWindowViewModel(
                promptEditor,
                historySessions,
                git,
                workspaceFooter);

            return new AppServices(
                mainWindowViewModel,
                sessionLifecycleService,
                sessionHistoryRefreshService,
                tmuxService);
        }

        private sealed class AppServices : IDisposable
        {
            private readonly SessionLifecycleService _sessionLifecycleService;
            private readonly SessionHistoryRefreshService _sessionHistoryRefreshService;
            private readonly TmuxService _tmuxService;
            private bool _isDisposed;

            public AppServices(
                MainWindowViewModel mainWindowViewModel,
                SessionLifecycleService sessionLifecycleService,
                SessionHistoryRefreshService sessionHistoryRefreshService,
                TmuxService tmuxService)
            {
                MainWindowViewModel = mainWindowViewModel;
                _sessionLifecycleService = sessionLifecycleService;
                _sessionHistoryRefreshService = sessionHistoryRefreshService;
                _tmuxService = tmuxService;
            }

            public MainWindowViewModel MainWindowViewModel { get; }

            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                _sessionHistoryRefreshService.Dispose();
                _sessionLifecycleService.Dispose();
                _tmuxService.Dispose();
            }
        }
    }
}
