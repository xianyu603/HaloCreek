using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HaloCreek.Logging;
using HaloCreek.Models;

namespace HaloCreek.Services.Completions
{
    public sealed class CompletionQuery : IDisposable
    {
        private const string LogCategory = "Completion";

        private readonly object _gate = new();
        private CompletionQuerySnapshot _current;
        private readonly CancellationTokenSource? _cancellation;
        private bool _isDisposed;

        private CompletionQuery(
            CompletionQuerySnapshot current,
            CancellationTokenSource? cancellation = null)
        {
            _current = current ?? throw new ArgumentNullException(nameof(current));
            _cancellation = cancellation;
        }

        public CompletionQuerySnapshot Current
        {
            get
            {
                lock (_gate)
                {
                    return _current;
                }
            }
        }

        public event EventHandler? Changed;

        internal static CompletionQuery Empty()
        {
            return new CompletionQuery(
                new CompletionQuerySnapshot(Array.Empty<PromptCompletionItem>()));
        }

        internal static CompletionQuery Start(
            IAsyncEnumerable<CompletionQuerySnapshot> snapshots,
            CancellationTokenSource cancellation)
        {
            ArgumentNullException.ThrowIfNull(snapshots);
            ArgumentNullException.ThrowIfNull(cancellation);

            var query = new CompletionQuery(
                new CompletionQuerySnapshot(Array.Empty<PromptCompletionItem>()),
                cancellation);
            // ReadSnapshotsAsync runs synchronously until the source stream's first incomplete await.
            // A synchronously yielded snapshot may update Current before the caller subscribes to
            // Changed, so callers must read Current once after Start returns.
            _ = query.ReadSnapshotsAsync(snapshots, cancellation.Token);
            return query;
        }

        private async Task ReadSnapshotsAsync(
            IAsyncEnumerable<CompletionQuerySnapshot> snapshots,
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var snapshot in snapshots.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    ApplyUpdate(snapshot);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory, ex, "Completion query stream failed.");
            }
        }

        private void ApplyUpdate(CompletionQuerySnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            EventHandler? changed;
            lock (_gate)
            {
                if (_isDisposed)
                {
                    return;
                }

                _current = snapshot;
                changed = Changed;
            }

            changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                Changed = null;
            }

            _cancellation?.Cancel();
        }
    }

    public sealed record CompletionQuerySnapshot(
        IReadOnlyList<PromptCompletionItem> Items);
}
