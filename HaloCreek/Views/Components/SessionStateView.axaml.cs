using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using LiveMarkdown.Avalonia;

namespace HaloCreek.Views.Components
{
    public partial class SessionStateView : UserControl
    {
        private const string LogCategory = "SessionStateView";
        private const string AbsolutePathLinkPrefix = "/abs/path/";
        private const double ScrollEndThreshold = 2;
        private bool _scrollToEndQueued;

        public ICommand OpenMarkdownLinkCommand { get; } = new MarkdownLinkCommand();

        public SessionStateView()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            QueueScrollToEnd();
        }

        private void MessagesScrollViewer_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            var contentWidth = MessagesScrollViewer.Bounds.Width
                - MessagesScrollViewer.Padding.Left
                - MessagesScrollViewer.Padding.Right;

            MessagesItemsControl.Width = Math.Max(0, contentWidth);
        }

        private void MessagesScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            var previousOffsetY = MessagesScrollViewer.Offset.Y - e.OffsetDelta.Y;
            var previousViewportHeight = MessagesScrollViewer.Viewport.Height - e.ViewportDelta.Y;
            var previousExtentHeight = MessagesScrollViewer.Extent.Height - e.ExtentDelta.Y;

            if ((e.ExtentDelta.Y > 0 || e.ViewportDelta.Y != 0)
                && IsScrolledToEnd(previousOffsetY, previousViewportHeight, previousExtentHeight))
            {
                QueueScrollToEnd();
            }
        }

        private static bool IsScrolledToEnd(
            double offsetY,
            double viewportHeight,
            double extentHeight)
        {
            return offsetY + viewportHeight >= extentHeight - ScrollEndThreshold;
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
                PlatformInfrastructure.OpenMarkdownLink(NormalizeMarkdownLinkHref(args.HRef));
            }
            catch (Exception ex) when (ex is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or ArgumentException
                or NotSupportedException)
            {
                Log.Error(LogCategory, ex, $"Failed to open markdown link. HRef={args.HRef.OriginalString}");
            }
        }

        private static Uri NormalizeMarkdownLinkHref(Uri href)
        {
            var target = href.OriginalString.Trim();
            if (target.StartsWith(AbsolutePathLinkPrefix, StringComparison.Ordinal))
            {
                return new Uri(target[AbsolutePathLinkPrefix.Length..], UriKind.RelativeOrAbsolute);
            }

            if (target.Length >= 4
                && target[0] == '/'
                && char.IsAsciiLetter(target[1])
                && target[2] == ':'
                && target[3] is '/' or '\\')
            {
                return new Uri(target[1..], UriKind.RelativeOrAbsolute);
            }

            return href;
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
