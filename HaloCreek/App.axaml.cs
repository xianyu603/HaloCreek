using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Services;
using HaloCreek.Services.Completions;
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
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;
                Dispatcher.UIThread.Post(async () => await InitializeMainWindowAsync(desktop, mainWindow));
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static async Task InitializeMainWindowAsync(
            IClassicDesktopStyleApplicationLifetime desktop,
            MainWindow mainWindow)
        {
            try
            {
                var appDisposeScope = await CreateAppDisposeScopeAsync(mainWindow);
                mainWindow.DataContext = appDisposeScope.MainWindowViewModel;
                desktop.Exit += (_, _) => appDisposeScope.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error("Startup", ex, "Application startup failed.");
                await new PlatformInfrastructure(mainWindow).ShowErrorDialogAsync(
                    "Startup failed",
                    ex.Message);
                desktop.Shutdown(1);
            }
        }

        private static async Task<AppDisposeScope> CreateAppDisposeScopeAsync(MainWindow mainWindow)
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
            WorkspaceRuntime.Initialize(
                appCommonRuntime,
                workspaceCacheService,
                configService);
            var workspaceStartupSelector = new WorkspaceStartupSelector(
                appCommonRuntime,
                workspaceCacheService);
            await workspaceStartupSelector.SelectRequiredWorkspaceAsync();
            var tmuxService = new TmuxService(appCommonRuntime);
            var terminalService = new TerminalService(appCommonRuntime);
            var sessionLifecycleService = new SessionLifecycleService(
                tmuxService,
                terminalService,
                appCommonRuntime);
            var gitService = new GitService();
            var reviewSnapshotService = new ReviewSnapshotService(gitService);
            var externalActionService = new ExternalActionService();
            var completionCoordinator = new CompletionCoordinator(
                new Dictionary<char, ICompletionSource>
                {
                    [ShortcutPhraseCompletionSource.TriggerCharacter] = new ShortcutPhraseCompletionSource(),
                });
            var floatingPromptService = new FloatingPromptService(
                sessionLifecycleService,
                appCommonRuntime,
                completionCoordinator);

            ISessionHistoryReader sessionHistoryReader = new CodexSessionHistoryReader(appCommonRuntime);
            var sessionHistoryQueryService = new SessionHistoryQueryService(sessionHistoryReader);
            var sessionHistoryRefreshService = new SessionHistoryRefreshService(sessionHistoryQueryService);

            var promptEditor = new PromptEditorViewModel(
                sessionLifecycleService,
                appCommonRuntime,
                completionCoordinator);
            var review = new ReviewViewModel(
                reviewSnapshotService,
                gitService,
                externalActionService,
                appCommonRuntime);
            var historySessions = new HistorySessionsViewModel(
                sessionHistoryRefreshService,
                sessionLifecycleService,
                appCommonRuntime);
            var logs = new LogPanelViewModel();
            var workspaceFooter = new WorkspaceFooterViewModel(appCommonRuntime);

            var mainWindowViewModel = new MainWindowViewModel(
                promptEditor,
                review,
                historySessions,
                logs,
                workspaceFooter);
            var globalHotkeyActionService = new GlobalHotkeyActionService(
                floatingPromptService,
                sessionLifecycleService);
            var globalHotkeyRegistrar = new GlobalHotkeyRegistrar();
            globalHotkeyRegistrar.MainWindowHotkeyPressed += (_, _) =>
                globalHotkeyActionService.ActivateFloatingPrompt();
            globalHotkeyRegistrar.FrontTerminalHotkeyPressed += (_, _) =>
                globalHotkeyActionService.ActivateFrontTerminal();
            globalHotkeyRegistrar.RegisterDefaultHotkeys();

            return new AppDisposeScope(
                mainWindowViewModel,
                promptEditor,
                workspaceFooter,
                review,
                logs,
                sessionLifecycleService,
                sessionHistoryRefreshService,
                tmuxService,
                floatingPromptService,
                globalHotkeyRegistrar);
        }

        // 统一收口应用级对象的退出释放。当前这里同时承载少量对象组装职责，
        // 后续如果创建流程继续变复杂，再拆出单独的 composition/root 类型。
        private sealed class AppDisposeScope : IDisposable
        {
            private readonly LogPanelViewModel _logs;
            private readonly PromptEditorViewModel _promptEditor;
            private readonly WorkspaceFooterViewModel _workspaceFooter;
            private readonly ReviewViewModel _review;
            private readonly SessionLifecycleService _sessionLifecycleService;
            private readonly SessionHistoryRefreshService _sessionHistoryRefreshService;
            private readonly TmuxService _tmuxService;
            private readonly FloatingPromptService _floatingPromptService;
            private readonly GlobalHotkeyRegistrar _globalHotkeyRegistrar;
            private bool _isDisposed;

            public AppDisposeScope(
                MainWindowViewModel mainWindowViewModel,
                PromptEditorViewModel promptEditor,
                WorkspaceFooterViewModel workspaceFooter,
                ReviewViewModel review,
                LogPanelViewModel logs,
                SessionLifecycleService sessionLifecycleService,
                SessionHistoryRefreshService sessionHistoryRefreshService,
                TmuxService tmuxService,
                FloatingPromptService floatingPromptService,
                GlobalHotkeyRegistrar globalHotkeyRegistrar)
            {
                MainWindowViewModel = mainWindowViewModel;
                _promptEditor = promptEditor ?? throw new ArgumentNullException(nameof(promptEditor));
                _workspaceFooter = workspaceFooter ?? throw new ArgumentNullException(nameof(workspaceFooter));
                _review = review ?? throw new ArgumentNullException(nameof(review));
                _logs = logs;
                _sessionLifecycleService = sessionLifecycleService;
                _sessionHistoryRefreshService = sessionHistoryRefreshService;
                _tmuxService = tmuxService;
                _floatingPromptService = floatingPromptService
                    ?? throw new ArgumentNullException(nameof(floatingPromptService));
                _globalHotkeyRegistrar = globalHotkeyRegistrar
                    ?? throw new ArgumentNullException(nameof(globalHotkeyRegistrar));
            }

            public MainWindowViewModel MainWindowViewModel { get; }

            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                _globalHotkeyRegistrar.Dispose();
                _floatingPromptService.Dispose();
                _workspaceFooter.Dispose();
                _promptEditor.Dispose();
                _review.Dispose();
                _logs.Dispose();
                _sessionHistoryRefreshService.Dispose();
                _sessionLifecycleService.Dispose();
                _tmuxService.Dispose();
            }
        }
    }
}
