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
                mainWindow.Opened += async (_, _) =>
                    await appServices.MainWindowViewModel.LoadConfigAndApplyDefaultWorkspaceAsync();
                desktop.Exit += (_, _) => appServices.Dispose();
                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static AppServices CreateAppServices(MainWindow mainWindow)
        {
            var platformInfrastructure = new PlatformInfrastructure(mainWindow);
            var configService = new ConfigService();
            var tmuxService = new TmuxService(platformInfrastructure);
            var terminalService = new TerminalService(platformInfrastructure);
            var sessionLifecycleService = new SessionLifecycleService(tmuxService, terminalService);
            var gitService = new GitService();

            ISessionHistoryReader sessionHistoryReader = new CodexSessionHistoryReader(platformInfrastructure);
            var sessionHistoryQueryService = new SessionHistoryQueryService(sessionHistoryReader, configService);
            var sessionHistoryRefreshService = new SessionHistoryRefreshService(sessionHistoryQueryService);

            var promptEditor = new PromptEditorViewModel(
                sessionLifecycleService,
                configService);
            var historySessions = new HistorySessionsViewModel(
                sessionHistoryRefreshService,
                sessionLifecycleService,
                configService);
            var git = new GitViewModel(gitService);
            var workspaceFooter = new WorkspaceFooterViewModel(platformInfrastructure);

            var mainWindowViewModel = new MainWindowViewModel(
                platformInfrastructure,
                configService,
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
                _sessionLifecycleService.Dispose();
                _sessionHistoryRefreshService.Dispose();
                _tmuxService.Dispose();
            }
        }
    }
}
