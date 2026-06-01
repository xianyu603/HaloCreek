using System;
using HaloCreek.Infrastructure;

namespace HaloCreek.Models
{
    public sealed record ReviewClipboardLocationResult(
        ReviewClipboardLocationStatus Status,
        ClipboardTextSnapshot ClipboardSnapshot,
        ReviewLocationCandidate? Location,
        string? FailureReason,
        string Message);

    public enum ReviewClipboardLocationStatus
    {
        UniqueMatch,
        Failed
    }

    public sealed record ReviewLocationCandidate(
        string RelativePath,
        int StartLine,
        int StartColumn,
        int EndLine,
        int EndColumn);

    public sealed class ReviewClipboardLocationChangedEventArgs : EventArgs
    {
        public ReviewClipboardLocationChangedEventArgs(ReviewClipboardLocationResult result)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public ReviewClipboardLocationResult Result { get; }
    }
}
