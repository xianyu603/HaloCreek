using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Logging;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class LogPanelViewModel : ViewModelBase, IDisposable
    {
        private string _logText;
        private bool _isDisposed;// 不喜欢这个 不过这个类足够简单 不纠结了

        public LogPanelViewModel()
        {
            _logText = CurrentLogText.GetSnapshotText();
            ClearCommand = new RelayCommand(Clear);
            CurrentLogText.TextChanged += OnLogTextChanged;
        }

        public string LogText
        {
            get => _logText;
            private set => SetProperty(ref _logText, value ?? string.Empty);
        }

        public IRelayCommand ClearCommand { get; }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            CurrentLogText.TextChanged -= OnLogTextChanged;
        }

        private void Clear()
        {
            CurrentLogText.Clear();
        }

        private void OnLogTextChanged(string snapshot)
        {
            if (_isDisposed)
            {
                return;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                LogText = snapshot;
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (!_isDisposed)
                {
                    LogText = snapshot;
                }
            });
        }
    }
}
