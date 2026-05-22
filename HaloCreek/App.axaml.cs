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
                var mainWindowViewModel = CreateMainWindowViewModel(mainWindow);
                mainWindow.DataContext = mainWindowViewModel;
                mainWindow.Opened += async (_, _) => await mainWindowViewModel.LoadConfigAndApplyDefaultWorkspaceAsync();
                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static MainWindowViewModel CreateMainWindowViewModel(MainWindow mainWindow)
        {
            var platformInfrastructure = new PlatformInfrastructure(mainWindow);
            var configService = new ConfigService();
            var tmuxService = new TmuxService();
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
            var ongoingSessions = new OngoingSessionsViewModel(sessionLifecycleService);
            var git = new GitViewModel(gitService);
            var workspaceFooter = new WorkspaceFooterViewModel(platformInfrastructure);

            var mainWindowViewModel = new MainWindowViewModel(
                platformInfrastructure,
                configService,
                promptEditor,
                historySessions,
                ongoingSessions,
                git,
                workspaceFooter);

            return mainWindowViewModel;
        }
    }
}
