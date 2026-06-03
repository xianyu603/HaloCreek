using System;
using HaloCreek.Infrastructure;

namespace HaloCreek.Models
{
    public sealed record ReviewClipboardClipLocateResult(
        ReviewClipboardClipLocateStatus Status,
        ClipboardTextSnapshot ClipboardSnapshot,
        ReviewClipLocateCandidate? ClipLocate,
        string? FailureReason,
        string Message)
    {
        public static string FormatResultText(ReviewClipboardClipLocateResult? result)
        {
            return result?.FormatResultText()
                ?? "Unmatched"
                + Environment.NewLine
                + "Failure reason: Unknown"
                + Environment.NewLine
                + "No clipboard clip locate result is available.";
        }

        public string FormatResultText()
        {
            if (Status == ReviewClipboardClipLocateStatus.UniqueMatch && ClipLocate is not null)
            {
                return "Matched"
                    + Environment.NewLine
                    + ClipLocate.FormatLocationText();
            }

            var failureReason = string.IsNullOrWhiteSpace(FailureReason)
                ? "Unknown"
                : FailureReason;
            var detail = string.IsNullOrWhiteSpace(Message)
                ? "No clipboard clip locate result is available."
                : Message;
            return "Unmatched"
                + Environment.NewLine
                + $"Failure reason: {failureReason}"
                + Environment.NewLine
                + detail;
        }
    }

    public enum ReviewClipboardClipLocateStatus
    {
        UniqueMatch,
        Failed
    }

    public sealed record ReviewClipLocateCandidate(
        string RelativePath,
        int StartLine,
        int StartColumn,
        int EndLine,
        int EndColumn)
    {
        public string FormatLocationText()
        {
            return $"{RelativePath}, line {StartLine} col {StartColumn} to line {EndLine} col {EndColumn}";
        }
    }

    public sealed class ReviewClipboardClipLocateChangedEventArgs : EventArgs
    {
        public ReviewClipboardClipLocateChangedEventArgs(ReviewClipboardClipLocateResult result)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public ReviewClipboardClipLocateResult Result { get; }
    }
}
