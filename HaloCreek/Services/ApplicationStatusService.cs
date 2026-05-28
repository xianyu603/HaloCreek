using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using HaloCreek.Logging;

namespace HaloCreek.Services
{
    public sealed class ApplicationStatusService
    {
        private readonly object _entriesLock = new();
        private readonly List<ApplicationStatusEntry> _entries = new();
        private long _nextSequence;
        private bool _isStatusDirty;
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

            lock (_entriesLock)
            {
                var removedCount = _entries.RemoveAll(entry => entry.Handle.Id == handle.Id);
                if (removedCount > 0)
                {
                    MarkStatusDirtyLocked();
                }
            }
        }

        private ApplicationStatusHandle AddStatus(ApplicationStatusKind kind, string message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(message);

            var handle = new ApplicationStatusHandle(Guid.NewGuid());

            lock (_entriesLock)
            {
                _entries.Add(new ApplicationStatusEntry(
                    handle,
                    kind,
                    message,
                    _nextSequence++));
                MarkStatusDirtyLocked();
            }

            return handle;
        }

        private void MarkStatusDirtyLocked()
        {
            if (_isStatusDirty)
            {
                return;
            }

            _isStatusDirty = true;
            Dispatcher.UIThread.Post(PublishCurrentStatusTextIfDirty);
        }

        private void PublishCurrentStatusTextIfDirty()
        {
            bool needPublish = false;
            lock (_entriesLock)
            {
                if (!_isStatusDirty)
                {
                    return;
                }

                _isStatusDirty = false;
                var nextStatusText = _entries
                    .OrderByDescending(entry => entry.Kind)
                    .ThenByDescending(entry => entry.Sequence)
                    .Select(entry => entry.Message)
                    .FirstOrDefault() ?? string.Empty;

                if (!string.Equals(_currentStatusText, nextStatusText, StringComparison.Ordinal))
                {
                    _currentStatusText = nextStatusText;
                    needPublish = true;
                }
            }

            if (!needPublish)
            {
                return;
            }

            try
            {
                StatusTextChanged?.Invoke(_currentStatusText);
            }
            catch (Exception ex)
            {
                Log.Error("ApplicationStatus", ex, "Status text subscriber failed.");
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
