using System;
using HaloCreek.Infrastructure;

namespace HaloCreek.Models
{
    public sealed record ReviewClipboardClipLocateResult(
        ReviewClipboardClipLocateStatus Status,
        ClipboardTextSnapshot ClipboardSnapshot,
        ReviewClipLocateCandidate? ClipLocate,
        string? FailureReason,
        string Message);

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
        int EndColumn);

    public sealed class ReviewClipboardClipLocateChangedEventArgs : EventArgs
    {
        public ReviewClipboardClipLocateChangedEventArgs(ReviewClipboardClipLocateResult result)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public ReviewClipboardClipLocateResult Result { get; }
    }
}
