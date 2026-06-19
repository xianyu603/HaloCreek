using System;
using System.Collections.Generic;
using System.Threading;

namespace HaloCreek.Services.Completions
{
    public interface ICompletionSource
    {
        IAsyncEnumerable<CompletionQuerySnapshot> StartQuery(
            string text,
            CancellationToken cancellationToken);
    }
}
