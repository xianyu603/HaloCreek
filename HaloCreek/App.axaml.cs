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
            var workspaceCacheService = new WorkspaceCacheService();
            var startupWorkspacePath = ResolveStartupWorkspacePath(platformInfrastructure, workspaceCacheService);
            var configService = new ConfigService();
            var tmuxService = new TmuxService(platformInfrastructure);
            var terminalService = new TerminalService(platformInfrastructure);
            var sessionLifecycleService = new SessionLifecycleService(tmuxService, terminalService);
            var applicationStatusService = new ApplicationStatusService();
            var transientEventService = new TransientEventService(platformInfrastructure);
            var gitService = new GitService();

            ISessionHistoryReader sessionHistoryReader = new CodexSessionHistoryReader(platformInfrastructure);
            var sessionHistoryQueryService = new SessionHistoryQueryService(sessionHistoryReader);
            var sessionHistoryRefreshService = new SessionHistoryRefreshService(sessionHistoryQueryService);

            var promptEditor = new PromptEditorViewModel(
                sessionLifecycleService,
                configService,
                transientEventService);
            var historySessions = new HistorySessionsViewModel(
                sessionHistoryRefreshService,
                sessionLifecycleService,
                configService,
                transientEventService);
            var git = new GitViewModel(
                gitService,
                configService,
                transientEventService);
            var workspaceFooter = new WorkspaceFooterViewModel(
                platformInfrastructure,
                applicationStatusService,
                transientEventService);

            var mainWindowViewModel = new MainWindowViewModel(
                startupWorkspacePath,
                workspaceCacheService,
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

        private static string ResolveStartupWorkspacePath(
            PlatformInfrastructure platformInfrastructure,
            WorkspaceCacheService workspaceCacheService)
        {
            ArgumentNullException.ThrowIfNull(platformInfrastructure);
            ArgumentNullException.ThrowIfNull(workspaceCacheService);

            var cachedWorkspacePath = workspaceCacheService.LoadLastWorkspacePath();
            if (platformInfrastructure.TryNormalizeExistingDirectoryPath(cachedWorkspacePath, out var normalizedPath))
            {
                return normalizedPath;
            }

            var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (platformInfrastructure.TryNormalizeExistingDirectoryPath(homePath, out normalizedPath))
            {
                return normalizedPath;
            }

            return AppContext.BaseDirectory;
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
