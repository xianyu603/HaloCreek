using System;
using System.IO;
using System.Threading.Tasks;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class ReviewClipboardContextService : IDisposable
    {
        private const string LogCategory = "ReviewClipLocate";
        private const long MaxCandidateFileBytes = 2 * 1024 * 1024;

        private readonly PlatformClipboardInfrastructure _platformClipboardInfrastructure;
        private readonly GitService _gitService;
        private readonly object SyncRoot = new();
        private long _latestScanId;
        private ReviewClipboardClipLocateResult? _currentClipLocateResult;
        private bool _isDisposed;

        public ReviewClipboardContextService(
            PlatformClipboardInfrastructure platformClipboardInfrastructure,
            GitService gitService)
        {
            _platformClipboardInfrastructure = platformClipboardInfrastructure
                ?? throw new ArgumentNullException(nameof(platformClipboardInfrastructure));
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));

            _platformClipboardInfrastructure.TextChanged += OnClipboardTextChanged;
            Log.Info(LogCategory, "Review clipboard context service created.");
        }

        public ReviewClipboardClipLocateResult? CurrentClipLocateResult
        {
            get
            {
                lock (SyncRoot)
                {
                    return _currentClipLocateResult;
                }
            }
        }

        public event EventHandler<ReviewClipboardClipLocateChangedEventArgs>? ClipLocateChanged;

        public void Dispose()
        {
            lock (SyncRoot)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
            }

            _platformClipboardInfrastructure.TextChanged -= OnClipboardTextChanged;
        }

        private void OnClipboardTextChanged(object? sender, ClipboardTextChangedEventArgs e)
        {
            long scanId;
            lock (SyncRoot)
            {
                if (_isDisposed)
                {
                    return;
                }

                _latestScanId++;
                scanId = _latestScanId;
            }

            _ = Task.Run(() => RunClipLocateScan(e.Snapshot, scanId));
        }

        private void RunClipLocateScan(ClipboardTextSnapshot snapshot, long scanId)
        {
            try
            {
                var result = Scan(snapshot);
                ApplyScanResult(result, scanId);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.ComponentModel.Win32Exception
                or InvalidOperationException)
            {
                var result = CreateFailureResult(snapshot, "ScanFailed", "Review clip locate scan failed.");
                if (ApplyScanResult(result, scanId))
                {
                    Log.Error(LogCategory, ex, "Review clip locate scan failed.");
                }
            }
        }

        private ReviewClipboardClipLocateResult Scan(ClipboardTextSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(snapshot.Text))
            {
                Log.Info(LogCategory, "Review clip locate scan ignored. reason=IgnoredEmptyClipboardText");
                return CreateFailureResult(
                    snapshot,
                    "IgnoredEmptyClipboardText",
                    "Clipboard text is empty or whitespace.");
            }

            var searchText = TrimTrailingNewlines(NormalizeLineEndings(snapshot.Text));
            if (string.IsNullOrWhiteSpace(searchText))
            {
                Log.Info(LogCategory, "Review clip locate scan ignored. reason=IgnoredEmptyClipboardText");
                return CreateFailureResult(
                    snapshot,
                    "IgnoredEmptyClipboardText",
                    "Clipboard text is empty or whitespace.");
            }

            var workspacePath = WorkspaceRuntime.Current.GitRootPath;
            var gitChanges = _gitService.GetChanges();

            Log.Info(
                LogCategory,
                $"Review clip locate scan started. clipboardLength={snapshot.Text.Length} workspace={QuoteForLog(workspacePath)}");

            var searchedFiles = 0;
            var skippedFiles = 0;
            ReviewClipLocateCandidate? firstMatch = null;

            foreach (var change in gitChanges.Changes)
            {
                // 这里写的有点绕 如果之后要改重构一下
                if (!ShouldSearchChange(change.ChangeType))
                {
                    continue;
                }

                if (!TryReadCandidateFile(workspacePath, change.RelativePath, out var content, out var skipped))
                {
                    skippedFiles += skipped ? 1 : 0;
                    continue;
                }

                searchedFiles++;
                var normalizedContent = NormalizeLineEndings(content);
                var firstIndex = normalizedContent.IndexOf(searchText, StringComparison.Ordinal);
                if (firstIndex < 0)
                {
                    continue;
                }

                var currentMatch = CreateClipLocateCandidate(
                    change.RelativePath,
                    normalizedContent,
                    firstIndex,
                    searchText.Length);
                var secondIndex = normalizedContent.IndexOf(
                    searchText,
                    firstIndex + 1,
                    StringComparison.Ordinal);
                if (secondIndex >= 0)
                {
                    var secondMatch = CreateClipLocateCandidate(
                        change.RelativePath,
                        normalizedContent,
                        secondIndex,
                        searchText.Length);
                    LogSkippedFiles(skippedFiles);
                    LogMultipleMatches(searchedFiles, currentMatch, secondMatch);
                    return CreateFailureResult(
                        snapshot,
                        "MultipleMatches",
                        "Clipboard text matched more than one clip locate candidate.");
                }

                if (firstMatch is not null)
                {
                    LogSkippedFiles(skippedFiles);
                    LogMultipleMatches(searchedFiles, firstMatch, currentMatch);
                    return CreateFailureResult(
                        snapshot,
                        "MultipleMatches",
                        "Clipboard text matched more than one clip locate candidate.");
                }

                firstMatch = currentMatch;
            }

            if (firstMatch is null)
            {
                LogSkippedFiles(skippedFiles);
                Log.Info(
                    LogCategory,
                    $"Review clip locate no match. searchedFiles={searchedFiles} skippedFiles={skippedFiles} clipboardLength={snapshot.Text.Length}");
                return CreateFailureResult(snapshot, "NoMatch", "Clipboard text did not match changed files.");
            }

            LogSkippedFiles(skippedFiles);
            Log.Info(
                LogCategory,
                "Review clip locate unique match. "
                + $"file={QuoteForLog(firstMatch.RelativePath)} "
                + $"startLine={firstMatch.StartLine} "
                + $"startColumn={firstMatch.StartColumn} "
                + $"endLine={firstMatch.EndLine} "
                + $"endColumn={firstMatch.EndColumn}");

            return new ReviewClipboardClipLocateResult(
                ReviewClipboardClipLocateStatus.UniqueMatch,
                snapshot,
                firstMatch,
                null,
                "Clipboard text matched one changed-file clip locate candidate.");
        }

        private bool ApplyScanResult(
            ReviewClipboardClipLocateResult result,
            long scanId)
        {
            EventHandler<ReviewClipboardClipLocateChangedEventArgs>? clipLocateChanged;
            lock (SyncRoot)
            {
                if (_isDisposed)
                {
                    return false;
                }

                if (scanId != _latestScanId)
                {
                    Log.Info(LogCategory, "Review clip locate scan result ignored because a newer clipboard text arrived.");
                    return false;
                }

                _currentClipLocateResult = result;
                clipLocateChanged = ClipLocateChanged;
            }

            clipLocateChanged?.Invoke(this, new ReviewClipboardClipLocateChangedEventArgs(result));
            return true;
        }

        private static bool TryReadCandidateFile(
            string workspacePath,
            string relativePath,
            out string content,
            out bool skipped)
        {
            content = string.Empty;
            skipped = false;

            var fullPath = Path.GetFullPath(Path.Combine(workspacePath, relativePath));
            if (!File.Exists(fullPath))
            {
                skipped = true;
                return false;
            }

            var fileInfo = new FileInfo(fullPath);
            if ((fileInfo.Attributes & FileAttributes.Directory) != 0
                || fileInfo.Length > MaxCandidateFileBytes)
            {
                skipped = true;
                return false;
            }

            try
            {
                content = File.ReadAllText(fullPath);
                return true;
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
            {
                skipped = true;
                return false;
            }
        }

        private static bool ShouldSearchChange(GitChangeType changeType)
        {
            return changeType is GitChangeType.Added
                or GitChangeType.Modified
                or GitChangeType.Renamed
                or GitChangeType.Copied
                or GitChangeType.Untracked;
        }

        private static ReviewClipLocateCandidate CreateClipLocateCandidate(
            string relativePath,
            string content,
            int startIndex,
            int matchLength)
        {
            var start = GetPosition(content, startIndex);
            var end = GetPosition(content, startIndex + matchLength - 1);
            return new ReviewClipLocateCandidate(
                relativePath,
                start.Line,
                start.Column,
                end.Line,
                end.Column);
        }

        // 后续出现更多文本处理任务时考虑抽Util
        private static TextPosition GetPosition(string text, int index)
        {
            var line = 1;
            var column = 1;
            for (var i = 0; i < index; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }

            return new TextPosition(line, column);
        }

        private static string NormalizeLineEndings(string text)
        {
            return text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal);
        }

        private static string TrimTrailingNewlines(string text)
        {
            var end = text.Length;
            while (end > 0 && text[end - 1] == '\n')
            {
                end--;
            }

            return end == text.Length ? text : text[..end];
        }

        private static ReviewClipboardClipLocateResult CreateFailureResult(
            ClipboardTextSnapshot snapshot,
            string failureReason,
            string message)
        {
            return new ReviewClipboardClipLocateResult(
                ReviewClipboardClipLocateStatus.Failed,
                snapshot,
                null,
                failureReason,
                message);
        }

        private static void LogMultipleMatches(
            int searchedFilesBeforeStop,
            ReviewClipLocateCandidate firstMatch,
            ReviewClipLocateCandidate secondMatch)
        {
            Log.Info(
                LogCategory,
                $"Review clip locate multiple matches. searchedFilesBeforeStop={searchedFilesBeforeStop}");
            Log.Info(LogCategory, FormatMatchLog("Review clip locate first match.", firstMatch));
            Log.Info(LogCategory, FormatMatchLog("Review clip locate second match.", secondMatch));
        }

        private static void LogSkippedFiles(int skippedFiles)
        {
            if (skippedFiles <= 0)
            {
                return;
            }

            Log.Info(LogCategory, $"Review clip locate skipped files. skippedFiles={skippedFiles}");
        }

        private static string FormatMatchLog(string prefix, ReviewClipLocateCandidate match)
        {
            return prefix
                + $" file={QuoteForLog(match.RelativePath)}"
                + $" startLine={match.StartLine}"
                + $" startColumn={match.StartColumn}"
                + $" endLine={match.EndLine}"
                + $" endColumn={match.EndColumn}";
        }

        private static string QuoteForLog(string value)
        {
            return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }

        private sealed record TextPosition(int Line, int Column);
    }
}
