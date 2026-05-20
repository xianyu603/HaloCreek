using System.Collections.Generic;
using HaloCreek.Models;

namespace HaloCreek.Services.SessionHistory
{
    public sealed record SessionHistoryResult(
        IReadOnlyList<HistorySessionInfo> Sessions,
        int SkippedFileCount);
}
