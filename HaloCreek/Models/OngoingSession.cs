using System;
using CommunityToolkit.Mvvm.ComponentModel;
using HaloCreek.Services.SessionState;

namespace HaloCreek.Models
{
    public sealed class OngoingSession : ObservableObject
    {
        private bool _isFront;
        private bool _isInteractive;
        private SessionStateSnapshot _stateSnapshot;

        internal OngoingSession(
            string id,
            string title,
            DateTimeOffset startedAt,
            bool isFront,
            SessionStateSnapshot stateSnapshot,
            bool isInteractive)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(title);

            Id = id;
            Title = title;
            StartedAt = startedAt;
            _isFront = isFront;
            _stateSnapshot = stateSnapshot ?? throw new ArgumentNullException(nameof(stateSnapshot));
            _isInteractive = isInteractive;
        }

        public string Id { get; }

        public string Title { get; }

        public DateTimeOffset StartedAt { get; }

        public bool IsFront
        {
            get => _isFront;
            internal set => SetProperty(ref _isFront, value);
        }

        public bool IsInteractive
        {
            get => _isInteractive;
            internal set => SetProperty(ref _isInteractive, value);
        }

        public SessionStateSnapshot StateSnapshot
        {
            get => _stateSnapshot;
            internal set
            {
                if (SetProperty(ref _stateSnapshot, value ?? throw new ArgumentNullException(nameof(value))))
                {
                    OnPropertyChanged(nameof(TaskState));
                    OnPropertyChanged(nameof(IsActive));
                }
            }
        }

        public SessionTaskState TaskState => StateSnapshot.State;

        public bool? IsActive => StateSnapshot.Active;
    }
}
