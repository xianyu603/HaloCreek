using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HaloCreek.Behaviors
{
    public sealed class ExplorerContextMenuBehavior
    {
        public static readonly AttachedProperty<ContextMenu?> RootContextMenuProperty =
            AvaloniaProperty.RegisterAttached<ExplorerContextMenuBehavior, ListBox, ContextMenu?>(
                "RootContextMenu");

        public static readonly AttachedProperty<ContextMenu?> ItemContextMenuProperty =
            AvaloniaProperty.RegisterAttached<ExplorerContextMenuBehavior, ListBox, ContextMenu?>(
                "ItemContextMenu");

        public static readonly AttachedProperty<bool> IsItemContentHitTargetProperty =
            AvaloniaProperty.RegisterAttached<ExplorerContextMenuBehavior, InputElement, bool>(
                "IsItemContentHitTarget");

        private static readonly AttachedProperty<bool> IsContextHandlerAttachedProperty =
            AvaloniaProperty.RegisterAttached<ExplorerContextMenuBehavior, ListBox, bool>(
                "IsContextHandlerAttached");

        private static readonly AttachedProperty<PointerContextRequest?> PendingPointerContextRequestProperty =
            AvaloniaProperty.RegisterAttached<ExplorerContextMenuBehavior, ListBox, PointerContextRequest?>(
                "PendingPointerContextRequest");

        private static readonly AttachedProperty<bool> IgnoreNextContextRequestedProperty =
            AvaloniaProperty.RegisterAttached<ExplorerContextMenuBehavior, ListBox, bool>(
                "IgnoreNextContextRequested");

        static ExplorerContextMenuBehavior()
        {
            RootContextMenuProperty.Changed.AddClassHandler<ListBox>(OnContextMenuChanged);
            ItemContextMenuProperty.Changed.AddClassHandler<ListBox>(OnItemContextMenuChanged);
        }

        private ExplorerContextMenuBehavior()
        {
        }

        public static ContextMenu? GetRootContextMenu(ListBox listBox)
        {
            return listBox.GetValue(RootContextMenuProperty);
        }

        public static void SetRootContextMenu(ListBox listBox, ContextMenu? value)
        {
            listBox.SetValue(RootContextMenuProperty, value);
        }

        public static ContextMenu? GetItemContextMenu(ListBox listBox)
        {
            return listBox.GetValue(ItemContextMenuProperty);
        }

        public static void SetItemContextMenu(ListBox listBox, ContextMenu? value)
        {
            listBox.SetValue(ItemContextMenuProperty, value);
        }

        public static bool GetIsItemContentHitTarget(InputElement inputElement)
        {
            return inputElement.GetValue(IsItemContentHitTargetProperty);
        }

        public static void SetIsItemContentHitTarget(InputElement inputElement, bool value)
        {
            inputElement.SetValue(IsItemContentHitTargetProperty, value);
        }

        private static void OnContextMenuChanged(ListBox listBox, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.GetNewValue<ContextMenu?>() is null
                || listBox.GetValue(IsContextHandlerAttachedProperty))
            {
                return;
            }

            listBox.AddHandler(
                InputElement.PointerPressedEvent,
                OnPointerPressed,
                RoutingStrategies.Tunnel,
                true);
            listBox.AddHandler(
                InputElement.PointerReleasedEvent,
                OnPointerReleased,
                RoutingStrategies.Tunnel,
                true);
            listBox.AddHandler(
                InputElement.ContextRequestedEvent,
                OnContextRequested,
                RoutingStrategies.Tunnel,
                true);
            listBox.SetValue(IsContextHandlerAttachedProperty, true);
        }

        private static void OnItemContextMenuChanged(ListBox listBox, AvaloniaPropertyChangedEventArgs e)
        {
            OnContextMenuChanged(listBox, e);
        }

        private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not ListBox listBox)
            {
                return;
            }

            var point = e.GetCurrentPoint(listBox);
            if (!point.Properties.IsRightButtonPressed)
            {
                listBox.SetValue(PendingPointerContextRequestProperty, null);
                return;
            }

            var hitVisual = listBox.InputHitTest(point.Position) as Visual;
            var item = FindOwningListBoxItem(listBox, hitVisual);
            listBox.SetValue(
                PendingPointerContextRequestProperty,
                item is null
                    ? null
                    : new PointerContextRequest(
                        item,
                        listBox.SelectedItem,
                        item.IsSelected,
                        IsInsideItemContentHitTarget(hitVisual, item)));
            e.Handled = true;
        }

        private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (sender is not ListBox listBox
                || e.InitialPressMouseButton != MouseButton.Right)
            {
                return;
            }

            var pointerRequest = listBox.GetValue(PendingPointerContextRequestProperty);
            if (pointerRequest is null)
            {
                return;
            }

            OpenContextMenuAtCurrentPointer(listBox, e.GetCurrentPoint(listBox).Position, pointerRequest);
            listBox.SetValue(PendingPointerContextRequestProperty, null);
            listBox.SetValue(IgnoreNextContextRequestedProperty, true);
            Dispatcher.UIThread.Post(() => listBox.SetValue(IgnoreNextContextRequestedProperty, false));
            e.Handled = true;
        }

        private static void OnContextRequested(object? sender, ContextRequestedEventArgs e)
        {
            if (sender is not ListBox listBox
                || !e.TryGetPosition(listBox, out var position))
            {
                return;
            }

            e.Handled = true;
            if (listBox.GetValue(IgnoreNextContextRequestedProperty))
            {
                listBox.SetValue(IgnoreNextContextRequestedProperty, false);
                return;
            }

            var pointerRequest = listBox.GetValue(PendingPointerContextRequestProperty);
            listBox.SetValue(PendingPointerContextRequestProperty, null);
            OpenContextMenuAtCurrentPointer(listBox, position, pointerRequest);
        }

        private static void OpenContextMenuAtCurrentPointer(
            ListBox listBox,
            Point position,
            PointerContextRequest? pointerRequest)
        {
            var hitVisual = listBox.InputHitTest(position) as Visual;
            var item = FindOwningListBoxItem(listBox, hitVisual);
            var itemMenu = GetItemContextMenu(listBox);

            if (item is not null
                && itemMenu is not null
                && ShouldOpenItemMenu(item, hitVisual, pointerRequest))
            {
                listBox.SelectedItem = GetItemValue(item);
                OpenMenu(itemMenu, item, GetItemValue(item));
                return;
            }

            if (GetRootContextMenu(listBox) is { } rootMenu)
            {
                RestoreSelectionForRootMenu(listBox, item, pointerRequest);
                OpenMenu(rootMenu, listBox, listBox.DataContext);
            }
        }

        private static ListBoxItem? FindOwningListBoxItem(ListBox listBox, Visual? visual)
        {
            var item = FindAncestorOrSelf<ListBoxItem>(visual);
            if (item is null)
            {
                return null;
            }

            return item.GetVisualAncestors().OfType<ListBox>().FirstOrDefault() == listBox
                ? item
                : null;
        }

        private static bool IsInsideItemContentHitTarget(Visual? visual, Visual item)
        {
            for (var current = visual; current is not null && current != item; current = current.GetVisualParent())
            {
                if (current is InputElement inputElement
                    && GetIsItemContentHitTarget(inputElement))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldOpenItemMenu(
            ListBoxItem item,
            Visual? hitVisual,
            PointerContextRequest? pointerRequest)
        {
            if (pointerRequest is not null && ReferenceEquals(pointerRequest.Item, item))
            {
                return pointerRequest.ItemWasSelected || pointerRequest.HitContent;
            }

            return item.IsSelected || IsInsideItemContentHitTarget(hitVisual, item);
        }

        private static void RestoreSelectionForRootMenu(
            ListBox listBox,
            ListBoxItem? item,
            PointerContextRequest? pointerRequest)
        {
            if (item is not null
                && pointerRequest is not null
                && ReferenceEquals(pointerRequest.Item, item)
                && !pointerRequest.ItemWasSelected
                && !pointerRequest.HitContent)
            {
                listBox.SelectedItem = pointerRequest.SelectedItemBeforePress;
            }
        }

        private static T? FindAncestorOrSelf<T>(Visual? visual)
            where T : Visual
        {
            if (visual is T match)
            {
                return match;
            }

            return visual?.GetVisualAncestors().OfType<T>().FirstOrDefault();
        }

        private static object? GetItemValue(ListBoxItem item)
        {
            return item.DataContext ?? item.Content;
        }

        private static void OpenMenu(ContextMenu menu, Control target, object? dataContext)
        {
            menu.Close();
            menu.Placement = PlacementMode.Pointer;
            menu.DataContext = dataContext;
            menu.Open(target);
        }

        private sealed record PointerContextRequest(
            ListBoxItem Item,
            object? SelectedItemBeforePress,
            bool ItemWasSelected,
            bool HitContent);
    }
}
