using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LiveMarkdown.Avalonia;

namespace HaloCreek.Views.Components
{
    internal static class MarkdownRendererLinkClickWorkaround
    {
        private static readonly AttachedProperty<bool> IsRendererLinkedProperty =
            AvaloniaProperty.RegisterAttached<SessionStateView, MarkdownRenderer, bool>(
                "IsRendererLinked");

        private static readonly AttachedProperty<bool> IsLinkedProperty =
            AvaloniaProperty.RegisterAttached<SessionStateView, MarkdownTextBlock, bool>(
                "IsLinked");

        private static readonly MethodInfo HitTestPointMethod =
            typeof(Link).GetMethod("HitTestPoint", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(Link).FullName, "HitTestPoint");

        public static void Attach(MarkdownRenderer renderer)
        {
            if (renderer.GetValue(IsRendererLinkedProperty))
            {
                return;
            }

            renderer.SetValue(IsRendererLinkedProperty, true);
            renderer.LayoutUpdated += (_, _) => AttachTextBlocks(renderer);
        }

        private static void AttachTextBlocks(MarkdownRenderer renderer)
        {
            foreach (var textBlock in renderer.GetVisualDescendants().OfType<MarkdownTextBlock>())
            {
                if (textBlock.GetValue(IsLinkedProperty))
                {
                    continue;
                }

                textBlock.SetValue(IsLinkedProperty, true);
                textBlock.AddHandler(
                    InputElement.PointerPressedEvent,
                    HandleTextBlockPointerPressed,
                    RoutingStrategies.Bubble);
            }
        }

        private static void HandleTextBlockPointerPressed(object? sender, PointerPressedEventArgs args)
        {
            if (args.Handled
                || sender is not MarkdownTextBlock textBlock
                || !args.GetCurrentPoint(textBlock).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (HitTestLink(textBlock, args.GetPosition(textBlock)) is not null)
            {
                args.Handled = true;
            }
        }

        private static Link? HitTestLink(MarkdownTextBlock textBlock, Point point)
        {
            return (Link?)HitTestPointMethod.Invoke(null, new object[] { textBlock.TextLayout, point });
        }
    }
}
