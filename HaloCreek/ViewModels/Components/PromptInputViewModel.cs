using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
                Title = "$skill",
                Description = "Skill shortcuts",
                Children =
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
                        Children =
                        [
                            new PromptCompletionItem
                            {
                                Title = "$skill:implementation:feature",
                                Description = "Feature implementation workflow",
                                InsertText = "$skill:implementation:feature",
                            },
                            new PromptCompletionItem
                            {
                                Title = "$skill:implementation:fix",
                                Description = "Bug fix workflow",
                                InsertText = "$skill:implementation:fix",
                            },
                        ],
                    },
                ],
            },
            new PromptCompletionItem
            {
                Title = "$file",
                Description = "Reference a workspace file",
                InsertText = "$file",
            },
            new PromptCompletionItem
            {
                Title = "$prompt",
                Description = "Prompt templates",
                Children =
                [
                    new PromptCompletionItem
                    {
                        Title = "$prompt:summary",
                        Description = "Summarize current context",
                        InsertText = "$prompt:summary",
                    },
                    new PromptCompletionItem
                    {
                        Title = "$prompt:tests",
                        Description = "Ask for focused verification",
                        InsertText = "$prompt:tests",
                    },
                ],
            },
        ];

        public const string DefaultPlaceholderText =
            "Type a prompt or drop files here. Ctrl+Enter launches, Alt+Enter sends to front.";

        private readonly SessionLifecycleService _sessionLifecycleService;
        private readonly TransientEventService _transientEventService;
        private string _promptText = string.Empty;
        private int _promptCaretIndex;
        private CompletionTriggerAnalysis _completionTrigger = CompletionTriggerAnalysis.Hidden("Initial");
        private readonly ObservableCollection<PromptCompletionMenuLevel> _completionMenuLevels =
        [
            new PromptCompletionMenuLevel(DefaultCompletionItems),
        ];
        private int _activeCompletionLevelIndex;
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

        public ObservableCollection<PromptCompletionMenuLevel> CompletionMenuLevels
        {
            get => _completionMenuLevels;
        }

        public IAsyncRelayCommand LaunchCommand { get; }

        public IRelayCommand SendToFrontCommand { get; }

        private void RefreshCompletionTrigger()
        {
            UpdateCompletionTrigger(PromptText, PromptCaretIndex);
        }

        private void UpdateCompletionTrigger(string? text, int caretIndex)
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
                ResetCompletionSelection();
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
                case Key.Right:
                    return OpenNextCompletionLevel();
                case Key.Left:
                    return CloseActiveCompletionLevel();
                case Key.Escape:
                    ResetCompletionSelection();
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
            ResetCompletionSelection();
            Log.Info(LogCategory, $"Completion menu hidden. Reason={reason}");
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
            TrimCompletionLevelsAfter(_activeCompletionLevelIndex);
        }

        private bool OpenNextCompletionLevel()
        {
            var selectedItem = ActiveCompletionLevel.SelectedItem;
            if (selectedItem is null || selectedItem.Children.Count == 0)
            {
                return false;
            }

            TrimCompletionLevelsAfter(_activeCompletionLevelIndex);
            var nextLevel = new PromptCompletionMenuLevel(selectedItem.Children)
            {
                SelectedItem = selectedItem.Children[0],
            };
            _completionMenuLevels.Add(nextLevel);
            _activeCompletionLevelIndex = _completionMenuLevels.Count - 1;
            Log.Info(
                LogCategory,
                $"Completion menu level opened. Level={_activeCompletionLevelIndex}, ParentTitle='{selectedItem.Title}', ItemCount={selectedItem.Children.Count}");
            return true;
        }

        private bool CloseActiveCompletionLevel()
        {
            if (_activeCompletionLevelIndex == 0)
            {
                return false;
            }

            _activeCompletionLevelIndex--;
            TrimCompletionLevelsAfter(_activeCompletionLevelIndex);
            Log.Info(LogCategory, $"Completion menu level closed. Level={_activeCompletionLevelIndex}");
            return true;
        }

        private void ResetCompletionSelection()
        {
            _activeCompletionLevelIndex = 0;
            _completionMenuLevels[0].SelectedItem = null;
            TrimCompletionLevelsAfter(0);
        }

        private PromptCompletionMenuLevel ActiveCompletionLevel
        {
            get => _completionMenuLevels[_activeCompletionLevelIndex];
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

        private void TrimCompletionLevelsAfter(int levelIndex)
        {
            var keepCount = levelIndex + 1;
            if (_completionMenuLevels.Count <= keepCount)
            {
                return;
            }

            while (_completionMenuLevels.Count > keepCount)
            {
                _completionMenuLevels.RemoveAt(_completionMenuLevels.Count - 1);
            }
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

        public string? InsertText { get; init; }

        public IReadOnlyList<PromptCompletionItem> Children { get; init; } = Array.Empty<PromptCompletionItem>();

        public bool HasChildren => Children.Count > 0;
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
