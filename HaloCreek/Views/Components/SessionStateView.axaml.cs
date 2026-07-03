using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using LiveMarkdown.Avalonia;

namespace HaloCreek.Views.Components
{
    public partial class SessionStateView : UserControl
    {
        private const string LogCategory = "SessionStateView";

        public ICommand OpenMarkdownLinkCommand { get; } = new MarkdownLinkCommand();

        public SessionStateView()
        {
            InitializeComponent();
        }

        private void MessagesScrollViewer_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            var contentWidth = MessagesScrollViewer.Bounds.Width
                - MessagesScrollViewer.Padding.Left
                - MessagesScrollViewer.Padding.Right;

            MessagesItemsControl.Width = Math.Max(0, contentWidth);
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
