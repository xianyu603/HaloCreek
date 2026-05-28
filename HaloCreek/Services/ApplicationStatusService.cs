using System;
using System.Collections.Generic;
using System.Linq;

namespace HaloCreek.Services
{
    public sealed class ApplicationStatusService
    {
        private readonly object _entriesLock = new();
        private readonly List<ApplicationStatusEntry> _entries = new();
        private long _nextSequence;
        private string _currentStatusText = string.Empty;

        public event Action<string>? StatusTextChanged;

        public ApplicationStatusHandle BeginBackgroundTask(string message)
        {
            return AddStatus(ApplicationStatusKind.BackgroundTask, message);
        }

        public ApplicationStatusHandle SetGlobalError(string message)
        {
            return AddStatus(ApplicationStatusKind.GlobalError, message);
        }

        public void Clear(ApplicationStatusHandle handle)
        {
            ArgumentNullException.ThrowIfNull(handle);

            string? changedStatusText = null;
            lock (_entriesLock)
            {
                var removedCount = _entries.RemoveAll(entry => entry.Handle.Id == handle.Id);
                if (removedCount > 0)
                {
                    changedStatusText = RecalculateCurrentStatusText();
                }
            }

            PublishIfChanged(changedStatusText);
        }

        // TODO 存在并发风险 lock解锁之后如果又另一个人直接publish 会导致后计算的被先计算的覆盖掉
        // 好像需要一个串行的publisher 不能在对外接口里面publish 之后再改
        private ApplicationStatusHandle AddStatus(ApplicationStatusKind kind, string message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);

            var handle = new ApplicationStatusHandle(Guid.NewGuid());
            string? changedStatusText;

            lock (_entriesLock)
            {
                _entries.Add(new ApplicationStatusEntry(
                    handle,
                    kind,
                    message,
                    _nextSequence++));
                changedStatusText = RecalculateCurrentStatusText();
            }

            PublishIfChanged(changedStatusText);
            return handle;
        }

        private string? RecalculateCurrentStatusText()
        {
            var nextStatusText = _entries
                .OrderByDescending(entry => entry.Kind)
                .ThenByDescending(entry => entry.Sequence)
                .Select(entry => entry.Message)
                .FirstOrDefault() ?? string.Empty;

            if (string.Equals(_currentStatusText, nextStatusText, StringComparison.Ordinal))
            {
                return null;
            }

            _currentStatusText = nextStatusText;
            return nextStatusText;
        }

        private void PublishIfChanged(string? statusText)
        {
            if (statusText is not null)
            {
                StatusTextChanged?.Invoke(statusText);
            }
        }

        private sealed record ApplicationStatusEntry(
            ApplicationStatusHandle Handle,
            ApplicationStatusKind Kind,
            string Message,
            long Sequence);

        private enum ApplicationStatusKind
        {
            BackgroundTask = 1,
            GlobalError = 2,
        }
    }
}
