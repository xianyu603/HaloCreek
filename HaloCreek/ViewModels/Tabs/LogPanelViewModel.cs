using System;
using CommunityToolkit.Mvvm.Input;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class LogPanelViewModel : ViewModelBase
    {
        private string _logText;

        public LogPanelViewModel()
        {
            _logText = CreatePreviewLogText();
            ClearCommand = new RelayCommand(Clear);
        }

        public string LogText
        {
            get => _logText;
            private set => SetProperty(ref _logText, value ?? string.Empty);
        }

        public IRelayCommand ClearCommand { get; }

        private void Clear()
        {
            LogText = string.Empty;
        }

        private static string CreatePreviewLogText()
        {
            return string.Join(
                Environment.NewLine,
                "2026-06-01T09:20:13.1040000+08:00 [Info] [Application] [T1] HaloCreek starting. LogFile=C:\\Users\\demo\\AppData\\Roaming\\HaloCreek\\Logs\\HaloCreek_20260601_092013_104_24856.log",
                "2026-06-01T09:20:13.2250000+08:00 [Info] [Workspace] [T1] Startup workspace initialized. Path=D:\\work\\HaloCreek",
                "2026-06-01T09:20:14.0180000+08:00 [Debug] [Git] [T8] Refresh requested for workspace D:\\work\\HaloCreek",
                "2026-06-01T09:20:14.1450000+08:00 [Info] [Git] [T8] Found 4 changed files.",
                "2026-06-01T09:20:15.0070000+08:00 [Warning] [Session] [T10] Session title is not available yet; keeping temporary display text.",
                "2026-06-01T09:20:16.3320000+08:00 [Error] [Terminal] [T12] Launch failed. Command=codex --sandbox workspace-write --approval never --very-long-argument-for-horizontal-scroll-preview=abcdefghijklmnopqrstuvwxyz-0123456789-ABCDEFGHIJKLMNOPQRSTUVWXYZ-D:\\work\\HaloCreek\\docs\\roadmap\\MVP2\\0-log-panel\\sample.txt",
                string.Empty);
        }
    }
}
