using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
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
        private const int ClipboardLogPreviewMaxLength = 80;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow();
                var appDisposeScope = CreateAppDisposeScope(mainWindow);
                mainWindow.DataContext = appDisposeScope.MainWindowViewModel;
                desktop.Exit += (_, _) => appDisposeScope.Dispose();
                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static AppDisposeScope CreateAppDisposeScope(MainWindow mainWindow)
        {
            var platformInfrastructure = new PlatformInfrastructure(mainWindow);
            PlatformClipboardInfrastructure platformClipboardInfrastructure;
            try
            {
                platformClipboardInfrastructure = new PlatformClipboardInfrastructure(mainWindow);
                Log.Info("Clipboard", "Clipboard infrastructure created.");
            }
            catch (Exception ex)
            {
                Log.Error("Clipboard", ex, "Clipboard infrastructure creation failed.");
                throw;
            }

            // TODO MVP2-A: Temporary clipboard watch for capability validation.
            // Remove this direct TextChanged subscription after ReviewClipboardContextService owns clipboard context.
            platformClipboardInfrastructure.TextChanged += (_, args) =>
            {
                var snapshot = args.Snapshot;
                Log.Info(
                    "Clipboard",
                    "Clipboard text changed. "
                    + $"length={snapshot.Text.Length} "
                    + $"capturedAt={snapshot.CapturedAt.ToString("O", CultureInfo.InvariantCulture)} "
                    + $"preview=\"{CreateClipboardLogPreview(snapshot.Text)}\"");
            };

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
            var logs = new LogPanelViewModel();
            var workspaceFooter = new WorkspaceFooterViewModel(
                workspaceRuntimeService,
                appCommonRuntime);

            var mainWindowViewModel = new MainWindowViewModel(
                promptEditor,
                historySessions,
                git,
                logs,
                workspaceFooter);

            return new AppDisposeScope(
                mainWindowViewModel,
                logs,
                sessionLifecycleService,
                sessionHistoryRefreshService,
                tmuxService,
                platformClipboardInfrastructure);
        }

        private static string CreateClipboardLogPreview(string text)
        {
            ArgumentNullException.ThrowIfNull(text);

            var isTruncated = text.Length > ClipboardLogPreviewMaxLength;
            var preview = isTruncated ? text[..ClipboardLogPreviewMaxLength] : text;
            preview = preview
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\t", "\\t", StringComparison.Ordinal);

            return isTruncated ? preview + "..." : preview;
        }

        // 统一收口应用级对象的退出释放。当前这里同时承载少量对象组装职责，
        // 后续如果创建流程继续变复杂，再拆出单独的 composition/root 类型。
        private sealed class AppDisposeScope : IDisposable
        {
            private readonly LogPanelViewModel _logs;
            private readonly SessionLifecycleService _sessionLifecycleService;
            private readonly SessionHistoryRefreshService _sessionHistoryRefreshService;
            private readonly TmuxService _tmuxService;
            private readonly PlatformClipboardInfrastructure _platformClipboardInfrastructure;
            private bool _isDisposed;

            public AppDisposeScope(
                MainWindowViewModel mainWindowViewModel,
                LogPanelViewModel logs,
                SessionLifecycleService sessionLifecycleService,
                SessionHistoryRefreshService sessionHistoryRefreshService,
                TmuxService tmuxService,
                PlatformClipboardInfrastructure platformClipboardInfrastructure)
            {
                MainWindowViewModel = mainWindowViewModel;
                _logs = logs;
                _sessionLifecycleService = sessionLifecycleService;
                _sessionHistoryRefreshService = sessionHistoryRefreshService;
                _tmuxService = tmuxService;
                _platformClipboardInfrastructure = platformClipboardInfrastructure
                    ?? throw new ArgumentNullException(nameof(platformClipboardInfrastructure));
            }

            public MainWindowViewModel MainWindowViewModel { get; }

            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                _platformClipboardInfrastructure.Dispose();
                _logs.Dispose();
                _sessionHistoryRefreshService.Dispose();
                _sessionLifecycleService.Dispose();
                _tmuxService.Dispose();
            }
        }
    }
}
