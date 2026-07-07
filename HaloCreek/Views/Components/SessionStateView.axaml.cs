using System;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Models;
using LiveMarkdown.Avalonia;

namespace HaloCreek.Views.Components
{
    public partial class SessionStateView : UserControl
    {
        private const string LogCategory = "SessionStateView";
        private OngoingSession? _session;
        private bool _scrollToEndQueued;

        public ICommand OpenMarkdownLinkCommand { get; } = new MarkdownLinkCommand();

        public SessionStateView()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (_session is not null)
            {
                _session.PropertyChanged -= Session_PropertyChanged;
            }

            _session = DataContext as OngoingSession;
            if (_session is not null)
            {
                _session.PropertyChanged += Session_PropertyChanged;
                QueueScrollToEnd();
            }
        }

        private void MessagesScrollViewer_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            var contentWidth = MessagesScrollViewer.Bounds.Width
                - MessagesScrollViewer.Padding.Left
                - MessagesScrollViewer.Padding.Right;

            MessagesItemsControl.Width = Math.Max(0, contentWidth);
        }

        private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OngoingSession.Messages))
            {
                QueueScrollToEnd();
            }
        }

        private void QueueScrollToEnd()
        {
            if (_scrollToEndQueued)
            {
                return;
            }

            _scrollToEndQueued = true;
            Dispatcher.UIThread.Post(
                () =>
                {
                    _scrollToEndQueued = false;
                    MessagesScrollViewer.ScrollToEnd();
                },
                DispatcherPriority.Loaded);
        }

        private void MarkdownRenderer_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is MarkdownRenderer renderer)
            {
                MarkdownRendererLinkClickWorkaround.Attach(renderer);
            }
        }

        private static void HandleMarkdownLink(LinkClickedEventArgs args)
        {
            if (args.HRef is null)
            {
                Log.Warning(LogCategory, "Markdown link click ignored because href is empty.");
                return;
            }

            Log.Info(LogCategory, $"Markdown LinkClick received. HRef={args.HRef.OriginalString}");

            try
            {
                PlatformInfrastructure.OpenMarkdownLink(args.HRef);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or ArgumentException
                or NotSupportedException)
            {
                Log.Error(LogCategory, ex, $"Failed to open markdown link. HRef={args.HRef.OriginalString}");
            }
        }

        private sealed class MarkdownLinkCommand : ICommand
        {
            public event EventHandler? CanExecuteChanged
            {
                add { }
                remove { }
            }

            public bool CanExecute(object? parameter)
            {
                return parameter is LinkClickedEventArgs;
            }

            public void Execute(object? parameter)
            {
                if (parameter is LinkClickedEventArgs args)
                {
                    HandleMarkdownLink(args);
                }
            }
        }
    }
}
