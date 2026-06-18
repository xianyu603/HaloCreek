using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Logging;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Components
{
    public sealed class PromptInputViewModel : ViewModelBase, IDisposable
    {
        private const string LogCategory = "PromptInput";
        private const char CompletionTriggerCharacter = '$';
        private static readonly PromptCompletionItem[] DefaultCompletionItems =
        [
            new PromptCompletionItem
            {
                Title = "$skill:code-review",
                Description = "Code review checklist",
                InsertText = "$skill:code-review",
            },
            new PromptCompletionItem
            {
                Title = "$skill:implementation",
                Description = "Implementation workflow",
                InsertText = "$skill:implementation",
            },
            new PromptCompletionItem
            {
                Title = "$file",
                Description = "Reference a workspace file",
                InsertText = "$file",
            },
        ];

        public const string DefaultPlaceholderText =
            "Type a prompt or drop files here. Ctrl+Enter launches, Alt+Enter sends to front.";

        private readonly SessionLifecycleService _sessionLifecycleService;
        private readonly TransientEventService _transientEventService;
        private string _promptText = string.Empty;
        private CompletionTriggerAnalysis _completionTrigger = CompletionTriggerAnalysis.Hidden("Initial");
        private PromptCompletionItem? _selectedCompletionItem;
        private bool _hasFrontSession;
        private bool _isDisposed;

        public PromptInputViewModel(
            SessionLifecycleService sessionLifecycleService,
            AppCommonRuntime appCommonRuntime)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _sessionLifecycleService = sessionLifecycleService
                ?? throw new ArgumentNullException(nameof(sessionLifecycleService));
            _transientEventService = appCommonRuntime.TransientEventService;

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
                }
            }
        }

        public string PlaceholderText => DefaultPlaceholderText;

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

        public IReadOnlyList<PromptCompletionItem> CompletionItems => DefaultCompletionItems;

        public PromptCompletionItem? SelectedCompletionItem
        {
            get => _selectedCompletionItem;
            set => SetProperty(ref _selectedCompletionItem, value);
        }

        public IAsyncRelayCommand LaunchCommand { get; }

        public IRelayCommand SendToFrontCommand { get; }

        public void UpdateCompletionTrigger(string? text, int caretIndex)
        {
            text ??= string.Empty;
            caretIndex = Math.Clamp(caretIndex, 0, text.Length);

            var analysis = AnalyzeCompletionTrigger(text, caretIndex);
            if (!analysis.IsValid)
            {
                HideCompletion(analysis.HideReason);
                return;
            }

            // 展开的时候点到另一个触发
            if (IsCompletionOpen && CompletionTokenStart != analysis.TokenStart)
            {
                HideCompletion("CaretOutsideToken");
            }

            var wasOpen = IsCompletionOpen;
            SetCompletionTrigger(analysis);

            if (!wasOpen)
            {
                SelectedCompletionItem = null;
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

            if (SelectedCompletionItem is null)
            {
                if (key != Key.Down)
                {
                    return false;
                }

                MoveCompletionSelection(1);
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
                case Key.Escape:
                    SelectedCompletionItem = null;
                    return true;
                default:
                    return false;
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _sessionLifecycleService.SessionsChanged -= RefreshFrontSessionState;
        }

        private static CompletionTriggerAnalysis AnalyzeCompletionTrigger(string text, int caretIndex)
        {
            if (caretIndex == 0)
            {
                return CompletionTriggerAnalysis.Hidden("NoTrigger");
            }

            var triggerIndex = text.LastIndexOf(CompletionTriggerCharacter, caretIndex - 1, caretIndex);
            if (triggerIndex < 0)
            {
                return CompletionTriggerAnalysis.Hidden("NoTrigger");
            }

            if (triggerIndex > 0 && !char.IsWhiteSpace(text[triggerIndex - 1]))
            {
                return CompletionTriggerAnalysis.Hidden("CharBeforeTrigger");
            }

            for (var index = triggerIndex + 1; index < caretIndex; index++)
            {
                if (char.IsWhiteSpace(text[index]))
                {
                    return CompletionTriggerAnalysis.Hidden("WhitespaceAfterTrigger");
                }
            }

            var tokenEnd = triggerIndex + 1;
            while (tokenEnd < text.Length && !char.IsWhiteSpace(text[tokenEnd]))
            {
                tokenEnd++;
            }

            if (caretIndex > tokenEnd)
            {
                return CompletionTriggerAnalysis.Hidden("CaretOutsideToken");
            }

            var filterText = text[(triggerIndex + 1)..tokenEnd];
            return CompletionTriggerAnalysis.Visible(triggerIndex, tokenEnd, filterText);
        }

        private void HideCompletion(string reason)
        {
            if (!IsCompletionOpen)
            {
                return;
            }

            SetCompletionTrigger(CompletionTriggerAnalysis.Hidden(reason));
            SelectedCompletionItem = null;
            Log.Info(LogCategory, $"Completion menu hidden. Reason={reason}");
        }

        private void MoveCompletionSelection(int step)
        {
            if (CompletionItems.Count == 0)
            {
                SelectedCompletionItem = null;
                return;
            }

            var currentIndex = SelectedCompletionItem is null
                ? -1
                : IndexOfCompletionItem(SelectedCompletionItem);
            var nextIndex = currentIndex < 0
                ? (step > 0 ? 0 : CompletionItems.Count - 1)
                : Math.Clamp(currentIndex + step, 0, CompletionItems.Count - 1);

            SelectedCompletionItem = CompletionItems[nextIndex];
        }

        private int IndexOfCompletionItem(PromptCompletionItem item)
        {
            for (var index = 0; index < CompletionItems.Count; index++)
            {
                if (ReferenceEquals(CompletionItems[index], item))
                {
                    return index;
                }
            }

            return -1;
        }

        private void SetCompletionTrigger(CompletionTriggerAnalysis analysis)
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
        }

        private readonly record struct CompletionTriggerAnalysis(
            bool IsValid,
            string HideReason,
            int TokenStart,
            int TokenEnd,
            string FilterText)
        {
            public static CompletionTriggerAnalysis Hidden(string reason)
            {
                return new CompletionTriggerAnalysis(false, reason, -1, -1, string.Empty);
            }

            public static CompletionTriggerAnalysis Visible(int tokenStart, int tokenEnd, string filterText)
            {
                return new CompletionTriggerAnalysis(true, string.Empty, tokenStart, tokenEnd, filterText);
            }
        }
    }

    public sealed class PromptCompletionItem
    {
        public required string Title { get; init; }

        public string? Description { get; init; }

        public required string InsertText { get; init; }

        public IReadOnlyList<PromptCompletionItem> Children { get; init; } = Array.Empty<PromptCompletionItem>();
    }
}
