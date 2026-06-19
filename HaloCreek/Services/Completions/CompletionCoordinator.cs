using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HaloCreek.Services.Completions
{
    public sealed class CompletionCoordinator
    {
        private readonly IReadOnlyDictionary<char, ICompletionSource> _sourcesByTrigger;

        public CompletionCoordinator()
            : this(new Dictionary<char, ICompletionSource>())
        {
        }

        public CompletionCoordinator(IReadOnlyDictionary<char, ICompletionSource> sourcesByTrigger)
        {
            ArgumentNullException.ThrowIfNull(sourcesByTrigger);

            _sourcesByTrigger = new Dictionary<char, ICompletionSource>(sourcesByTrigger);
            TriggerCharacters = _sourcesByTrigger.Keys.ToArray();
        }

        public IReadOnlyCollection<char> TriggerCharacters { get; }

        public CompletionQuery StartQuery(char trigger, string text)
        {
            text ??= string.Empty;
            if (!_sourcesByTrigger.TryGetValue(trigger, out var source))
            {
                return CompletionQuery.Empty();
            }

            var cancellation = new CancellationTokenSource();
            var snapshots = source.StartQuery(text, cancellation.Token);
            return CompletionQuery.StartPending(snapshots, cancellation);
        }
    }
}
