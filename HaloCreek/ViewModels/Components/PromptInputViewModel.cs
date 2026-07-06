using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services;
using HaloCreek.Services.Completions;
using HaloCreek.Services.PromptTemplates;
using HaloCreek.Services.SessionHistory;
using HaloCreek.Services.SessionState;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.ViewModels.Components
{
    public sealed class PromptInputViewModel : ViewModelBase, IDisposable
    {
        private const string LogCategory = "PromptInput";

        public const string DefaultPlaceholderText =
            "Type a prompt or drop files here. Ctrl+Enter launches, Alt+Enter sends to front.";

        private readonly SessionLifecycleService _sessionLifecycleService;
        private readonly TransientEventService _transientEventService;
        private readonly CompletionCoordinator _completionCoordinator;
        private string _promptText = string.Empty;
        private int _promptCaretIndex;
        private CompletionTriggerState _completionTrigger = CompletionTriggerState.Hidden("Initial");
        private readonly List<PromptCompletionMenuLevel> _completionMenuLevels =
        [
            new PromptCompletionMenuLevel(Array.Empty<PromptCompletionItem>()),
        ];
        private OngoingSessionInfo? _frontSession;
        private SessionStateViewModel _frontSessionState =
            SessionStateViewModel.CreateEmpty();
        private bool _hasFrontSession;
        private bool _isDisposed;

        public PromptInputViewModel(
            SessionLifecycleService sessionLifecycleService,
            AppCommonRuntime appCommonRuntime,
            CompletionCoordinator completionCoordinator,
            IWorkspaceSnapshotSource<SessionHistorySnapshot> historySnapshots)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _sessionLifecycleService = sessionLifecycleService
                ?? throw new ArgumentNullException(nameof(sessionLifecycleService));
            _transientEventService = appCommonRuntime.TransientEventService;
            _completionCoordinator = completionCoordinator
                ?? throw new ArgumentNullException(nameof(completionCoordinator));
            TemplatePicker = new PromptTemplatePickerViewModel(
                PromptTemplateStaticConfig.Items,
                historySnapshots);

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
                    RefreshCompletionTrigger();
                }
            }
        }

        public int PromptCaretIndex
        {
            get => _promptCaretIndex;
            set
            {
                if (SetProperty(ref _promptCaretIndex, value))
                {
                    RefreshCompletionTrigger();
                }
            }
        }

        public string PlaceholderText => DefaultPlaceholderText;

        public PromptTemplatePickerViewModel TemplatePicker { get; }

        public SessionStateViewModel FrontSessionState
        {
            get => _frontSessionState;
            private set => SetProperty(ref _frontSessionState, value);
        }

        public bool IsCompletionOpen
        {
            get => _completionTrigger.IsValid;
        }

        public int CompletionTokenStart
        {
            get => _completionTrigger.TokenStart;
        }

        public int CompletionTokenEnd
        {
            get => _completionTrigger.TokenEnd;
        }

        public string CompletionFilterText
        {
            get => _completionTrigger.FilterText;
        }

        public string CompletionPreviewText
        {
            get => ActiveCompletionLevel.SelectedItem?.Description ?? string.Empty;
        }

        public bool IsCompletionPreviewVisible
        {
            get => IsCompletionOpen && !string.IsNullOrWhiteSpace(CompletionPreviewText);
        }

        public PromptCompletionMenuLevel ActiveCompletionLevel
        {
            get => _completionMenuLevels[^1];
        }

        public IAsyncRelayCommand LaunchCommand { get; }

        public IRelayCommand SendToFrontCommand { get; }

        // 在真正干活之前触发 消息处理抛异常会导致发送失败 不过目前风险可控
        public event EventHandler? PromptSubmitted;

        private IWorkspaceSnapshotSource<SessionStateSnapshot>? FrontSessionStateSnapshots
            => _frontSession?.StateSnapshots;

        private void RefreshCompletionTrigger()
        {
            UpdateCompletionTrigger(PromptText, PromptCaretIndex);
        }

        private void UpdateCompletionTrigger(string? text, int caretIndex)
        {
            text ??= string.Empty;
            caretIndex = Math.Clamp(caretIndex, 0, text.Length);

            var triggerState = AnalyzeCompletionTrigger(
                text,
                caretIndex,
                _completionCoordinator.TriggerCharacters);
            if (!triggerState.IsValid)
            {
                HideCompletion(triggerState.HideReason);
                return;
            }

            var wasOpen = IsCompletionOpen;
            var activeQuery = _completionTrigger.Query;
            var queryChanged = activeQuery is null
                || CompletionTokenStart != triggerState.TokenStart
                || _completionTrigger.TriggerCharacter != triggerState.TriggerCharacter
                || !string.Equals(_completionTrigger.FilterText, triggerState.FilterText, StringComparison.Ordinal);

            if (queryChanged)
            {
                ResetCompletionSelection();
                activeQuery = StartCompletionQuery(triggerState);
            }

            SetCompletionTrigger(triggerState.WithQuery(activeQuery));

            if (!wasOpen && IsCompletionOpen)
            {
                Log.Info(
                    LogCategory,
                    $"Completion menu shown. TokenStart={CompletionTokenStart}, CaretIndex={caretIndex}, FilterText='{CompletionFilterText}'");
            }
        }

        public bool HandleCompletionNavigation(Key key)
        {
            if (!IsCompletionOpen)
            {
                return false;
            }

            var activeLevel = ActiveCompletionLevel;
            if (activeLevel.SelectedItem is null)
            {
                if (key != Key.Down)
                {
                    return false;
                }
                if (activeLevel.Items.Count != 0)
                {
                    MoveCompletionSelection(1);
                }
                // 否则说明没有任何匹配结果 忽略按键即可
                return true;
            }

            switch (key)
            {
                case Key.Down:
                    MoveCompletionSelection(1);
                    return true;
                case Key.Up:
                    MoveCompletionSelection(-1);
                    return true;
                case Key.Right:
                    return OpenNextCompletionLevel();
                case Key.Left:
                    return CloseActiveCompletionLevel();
                case Key.Escape:
                    ResetCompletionSelection();
                    var query = _completionTrigger.Query
                        ?? throw new InvalidOperationException("Completion query is required while the completion menu is open.");
                    ApplyCompletionSnapshot(query);
                    return true;
                case Key.Enter:
                    if (activeLevel.SelectedItem.InsertText is null && activeLevel.SelectedItem.Children.Count > 0)
                    {
                        OpenNextCompletionLevel();
                    }
                    else
                    {
                        AcceptSelectedCompletion();
                    }

                    return true;
                default:
                    return false;
            }
        }

        private void AcceptSelectedCompletion()
        {
            if (!IsCompletionOpen)
            {
                throw new InvalidOperationException("Cannot accept a completion item when the completion menu is closed.");
            }

            var selectedItem = ActiveCompletionLevel.SelectedItem
                ?? throw new InvalidOperationException("Cannot accept a completion item before one is selected.");

            var insertText = selectedItem.InsertText
                ?? throw new InvalidOperationException("Cannot accept a completion item without insertion text.");

            if (CompletionTokenStart < 0)
            {
                throw new InvalidOperationException("Cannot accept a completion item without an active completion token.");
            }

            var text = PromptText;
            var replaceStart = Math.Clamp(CompletionTokenStart, 0, text.Length);
            var replaceEnd = replaceStart;
            while (replaceEnd < text.Length && !char.IsWhiteSpace(text[replaceEnd]))
            {
                replaceEnd++;
            }

            var replacement = insertText + " ";
            var updatedText = text[..replaceStart] + replacement + text[replaceEnd..];
            var updatedCaretIndex = replaceStart + replacement.Length;

            Log.Info(
                LogCategory,
                $"Completion item accepted. Title='{selectedItem.Title}', ReplaceStart={replaceStart}, ReplaceEnd={replaceEnd}");

            PromptText = updatedText;
            PromptCaretIndex = updatedCaretIndex;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _sessionLifecycleService.SessionsChanged -= RefreshFrontSessionState;
            StopFrontSessionStateSubscription();
            StopActiveCompletionQuery();
        }

        private static CompletionTriggerState AnalyzeCompletionTrigger(
            string text,
            int caretIndex,
            IReadOnlyCollection<char> triggerCharacters)
        {
            if (caretIndex == 0 || triggerCharacters.Count == 0)
            {
                return CompletionTriggerState.Hidden("NoTrigger");
            }

            var triggerIndex = -1;
            for (var index = caretIndex - 1; index >= 0; index--)
            {
                foreach (var triggerCharacter in triggerCharacters)
                {
                    if (text[index] != triggerCharacter)
                    {
                        continue;
                    }

                    triggerIndex = index;
                    break;
                }

                if (triggerIndex >= 0)
                {
                    break;
                }
            }

            if (triggerIndex < 0)
            {
                return CompletionTriggerState.Hidden("NoTrigger");
            }

            if (triggerIndex > 0 && !char.IsWhiteSpace(text[triggerIndex - 1]))
            {
                return CompletionTriggerState.Hidden("CharBeforeTrigger");
            }

            for (var index = triggerIndex + 1; index < caretIndex; index++)
            {
                if (char.IsWhiteSpace(text[index]))
                {
                    return CompletionTriggerState.Hidden("WhitespaceAfterTrigger");
                }
            }

            var tokenEnd = triggerIndex + 1;
            while (tokenEnd < text.Length && !char.IsWhiteSpace(text[tokenEnd]))
            {
                tokenEnd++;
            }

            if (caretIndex > tokenEnd)
            {
                return CompletionTriggerState.Hidden("CaretOutsideToken");
            }

            var filterText = text[(triggerIndex + 1)..tokenEnd];
            return CompletionTriggerState.Visible(
                text[triggerIndex],
                triggerIndex,
                tokenEnd,
                filterText);
        }

        private void HideCompletion(string reason)
        {
            if (!IsCompletionOpen)
            {
                return;
            }

            StopActiveCompletionQuery();
            SetCompletionTrigger(CompletionTriggerState.Hidden(reason));
            ResetCompletionSelection();
            Log.Info(LogCategory, $"Completion menu hidden. Reason={reason}");
        }

        private CompletionQuery StartCompletionQuery(CompletionTriggerState triggerState)
        {
            StopActiveCompletionQuery();

            var query = _completionCoordinator.StartQuery(
                triggerState.TriggerCharacter,
                triggerState.FilterText);
            query.Changed += (_, _) => HandleCompletionQueryChanged(query);
            ApplyCompletionSnapshot(query);
            return query;
        }

        private void StopActiveCompletionQuery()
        {
            _completionTrigger.Query?.Dispose();
        }

        private void HandleCompletionQueryChanged(CompletionQuery query)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => HandleCompletionQueryChanged(query));
                return;
            }

            if (_isDisposed || !ReferenceEquals(query, _completionTrigger.Query))
            {
                return;
            }

            if (IsCompletionNavigating)
            {
                return;
            }

            ApplyCompletionSnapshot(query);
        }

        private void ApplyCompletionSnapshot(CompletionQuery query)
        {
            _completionMenuLevels.Clear();
            _completionMenuLevels.Add(new PromptCompletionMenuLevel(query.Current.Items));
            OnPropertyChanged(nameof(ActiveCompletionLevel));
            NotifyCompletionPreviewChanged();
        }

        private bool IsCompletionNavigating
        {
            get => _completionMenuLevels[^1].SelectedItem is not null;
        }

        private void MoveCompletionSelection(int step)
        {
            var activeLevel = ActiveCompletionLevel;
            if (activeLevel.Items.Count == 0)
            {
                throw new InvalidOperationException("Active completion menu level cannot be empty.");
            }

            var currentIndex = activeLevel.SelectedItem is null
                ? -1
                : IndexOfCompletionItem(activeLevel.Items, activeLevel.SelectedItem);
            var nextIndex = currentIndex < 0
                ? (step > 0 ? 0 : activeLevel.Items.Count - 1)
                : Math.Clamp(currentIndex + step, 0, activeLevel.Items.Count - 1);

            activeLevel.SelectedItem = activeLevel.Items[nextIndex];
            NotifyCompletionPreviewChanged();
        }

        private bool OpenNextCompletionLevel()
        {
            var selectedItem = ActiveCompletionLevel.SelectedItem;
            if (selectedItem is null || selectedItem.Children.Count == 0)
            {
                return false;
            }

            var nextLevel = new PromptCompletionMenuLevel(selectedItem.Children)
            {
                SelectedItem = selectedItem.Children[0],
            };
            _completionMenuLevels.Add(nextLevel);
            OnPropertyChanged(nameof(ActiveCompletionLevel));
            NotifyCompletionPreviewChanged();
            Log.Info(
                LogCategory,
                $"Completion menu level opened. Level={_completionMenuLevels.Count - 1}, ParentTitle='{selectedItem.Title}', ItemCount={selectedItem.Children.Count}");
            return true;
        }

        private bool CloseActiveCompletionLevel()
        {
            if (_completionMenuLevels.Count == 1)
            {
                return false;
            }

            _completionMenuLevels.RemoveAt(_completionMenuLevels.Count - 1);
            OnPropertyChanged(nameof(ActiveCompletionLevel));
            NotifyCompletionPreviewChanged();
            Log.Info(LogCategory, $"Completion menu level closed. Level={_completionMenuLevels.Count - 1}");
            return true;
        }

        private void ResetCompletionSelection()
        {
            _completionMenuLevels[0].SelectedItem = null;
            while (_completionMenuLevels.Count > 1)
            {
                _completionMenuLevels.RemoveAt(_completionMenuLevels.Count - 1);
            }

            OnPropertyChanged(nameof(ActiveCompletionLevel));
            NotifyCompletionPreviewChanged();
        }

        private static int IndexOfCompletionItem(IReadOnlyList<PromptCompletionItem> items, PromptCompletionItem item)
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (ReferenceEquals(items[index], item))
                {
                    return index;
                }
            }

            return -1;
        }

        private void NotifyCompletionPreviewChanged()
        {
            OnPropertyChanged(nameof(CompletionPreviewText));
            OnPropertyChanged(nameof(IsCompletionPreviewVisible));
        }

        private void SetCompletionTrigger(CompletionTriggerState analysis)
        {
            if (_completionTrigger == analysis)
            {
                return;
            }

            var previous = _completionTrigger;
            _completionTrigger = analysis;

            if (previous.IsValid != analysis.IsValid)
            {
                OnPropertyChanged(nameof(IsCompletionOpen));
                OnPropertyChanged(nameof(IsCompletionPreviewVisible));
            }

            if (previous.TokenStart != analysis.TokenStart)
            {
                OnPropertyChanged(nameof(CompletionTokenStart));
            }

            if (previous.TokenEnd != analysis.TokenEnd)
            {
                OnPropertyChanged(nameof(CompletionTokenEnd));
            }

            if (previous.FilterText != analysis.FilterText)
            {
                OnPropertyChanged(nameof(CompletionFilterText));
            }
        }

        private async Task LaunchAsync()
        {
            try
            {
                PromptSubmitted?.Invoke(this, EventArgs.Empty);
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
            PromptSubmitted?.Invoke(this, EventArgs.Empty);
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

            SetFrontSession(_sessionLifecycleService.GetFrontSessionInfo());
            ApplyFrontSessionState();
        }

        private void SetFrontSession(OngoingSessionInfo? frontSession)
        {
            if (!ReferenceEquals(FrontSessionStateSnapshots, frontSession?.StateSnapshots))
            {
                StopFrontSessionStateSubscription();
                _frontSession = frontSession;
                if (FrontSessionStateSnapshots is not null)
                {
                    FrontSessionStateSnapshots.Changed += HandleFrontSessionStateChanged;
                }
                return;
            }

            _frontSession = frontSession;
        }

        private void StopFrontSessionStateSubscription()
        {
            if (FrontSessionStateSnapshots is null)
            {
                return;
            }

            FrontSessionStateSnapshots.Changed -= HandleFrontSessionStateChanged;
            _frontSession = null;
        }

        private void HandleFrontSessionStateChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(ApplyFrontSessionState);
                return;
            }

            ApplyFrontSessionState();
        }

        private void ApplyFrontSessionState()
        {
            if (_frontSession is null)
            {
                FrontSessionState.UpdateEmpty();
                return;
            }

            FrontSessionState.UpdateFromSnapshot(
                _frontSession,
                _frontSession.StateSnapshots?.Current ?? SessionStateSnapshot.CreateEmpty());
        }

        private readonly record struct CompletionTriggerState(
            bool IsValid,
            string HideReason,
            CompletionQuery? Query,
            char TriggerCharacter,
            int TokenStart,
            int TokenEnd,
            string FilterText)
        {
            public static CompletionTriggerState Hidden(string reason)
            {
                return new CompletionTriggerState(false, reason, null, '\0', -1, -1, string.Empty);
            }

            public static CompletionTriggerState Visible(
                char triggerCharacter,
                int tokenStart,
                int tokenEnd,
                string filterText)
            {
                return new CompletionTriggerState(
                    true,
                    string.Empty,
                    null,
                    triggerCharacter,
                    tokenStart,
                    tokenEnd,
                    filterText);
            }

            public CompletionTriggerState WithQuery(CompletionQuery? query)
            {
                return this with
                {
                    Query = query,
                };
            }
        }
    }

    public sealed class PromptCompletionMenuLevel : ViewModelBase
    {
        private PromptCompletionItem? _selectedItem;

        public PromptCompletionMenuLevel(IReadOnlyList<PromptCompletionItem> items)
        {
            Items = items;
        }

        public IReadOnlyList<PromptCompletionItem> Items { get; }

        public PromptCompletionItem? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }
    }
}
