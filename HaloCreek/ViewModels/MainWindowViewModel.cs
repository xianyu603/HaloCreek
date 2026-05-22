// agent 开发平台

using System;
using System.Threading.Tasks;
using HaloCreek.Infrastructure;
using HaloCreek.Models;
using HaloCreek.Services;
using HaloCreek.ViewModels.Components;
using HaloCreek.ViewModels.Tabs;

namespace HaloCreek.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private const string ConfigErrorDialogTitle = "Config error";
        private const int PromptEditorTabIndex = 0;

        private readonly PlatformInfrastructure _platformInfrastructure;
        private readonly ConfigService _configService;
        private string? _currentWorkspacePath;
        private int _selectedTabIndex;

        public MainWindowViewModel(
            PlatformInfrastructure platformInfrastructure,
            ConfigService configService,
            PromptEditorViewModel promptEditor,
            HistorySessionsViewModel historySessions,
            GitViewModel git,
            WorkspaceFooterViewModel workspaceFooter)
        {
            _platformInfrastructure = platformInfrastructure ?? throw new ArgumentNullException(nameof(platformInfrastructure));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            PromptEditor = promptEditor;
            HistorySessions = historySessions;
            Git = git;
            WorkspaceFooter = workspaceFooter;

            WorkspaceFooter.SetWorkspaceDispatcher(ApplyValidatedWorkspacePath);
            PromptEditor.SetStatusDispatcher(message => WorkspaceFooter.StatusText = message);
            HistorySessions.SetStatusDispatcher(message => WorkspaceFooter.StatusText = message);
            HistorySessions.SetReeditInitialPromptDispatcher(ReeditInitialPrompt);
        }

        public PromptEditorViewModel PromptEditor { get; }

        public HistorySessionsViewModel HistorySessions { get; }

        public GitViewModel Git { get; }

        public WorkspaceFooterViewModel WorkspaceFooter { get; }

        public string? CurrentWorkspacePath
        {
            get => _currentWorkspacePath;
            private set => SetProperty(ref _currentWorkspacePath, value);
        }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public async Task LoadConfigAndApplyDefaultWorkspaceAsync()
        {
            // TODO 思考一下workspace没有的时候如何聚合global和workspace config
            var defaultWorkspacePath = _configService.LoadEffectiveConfig(null).DefaultWorkspacePath;
            if (string.IsNullOrWhiteSpace(defaultWorkspacePath))
            {
                return;
            }

            if (!_platformInfrastructure.TryNormalizeExistingDirectoryPath(defaultWorkspacePath, out var normalizedPath))
            {
                await ShowConfigErrorAsync(
                    $"DefaultWorkspacePath in config is not an existing directory: {defaultWorkspacePath}");
                return;
            }

            ApplyValidatedWorkspacePath(normalizedPath);
        }

        private Task ShowConfigErrorAsync(string message)
        {
            return _platformInfrastructure.ShowErrorDialogAsync(ConfigErrorDialogTitle, message);
        }

        public void ApplyValidatedWorkspacePath(string workspacePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

            CurrentWorkspacePath = workspacePath;

            PromptEditor.SetWorkspacePath(workspacePath);
            HistorySessions.SetWorkspacePath(workspacePath);
            Git.SetWorkspacePath(workspacePath);
            WorkspaceFooter.SetWorkspacePath(workspacePath);
        }

        private void ReeditInitialPrompt(HistorySessionInfo session)
        {
            ArgumentNullException.ThrowIfNull(session);

            if (string.IsNullOrWhiteSpace(session.InitialPrompt))
            {
                throw new InvalidOperationException("Initial prompt is empty.");
            }

            PromptEditor.PromptText = session.InitialPrompt;
            SelectedTabIndex = PromptEditorTabIndex;
            WorkspaceFooter.StatusText = "Initial prompt copied to Prompt Editor.";
        }
    }
}
