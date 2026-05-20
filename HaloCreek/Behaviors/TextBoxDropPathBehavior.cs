using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace HaloCreek.Behaviors
{
    public sealed class TextBoxDropPathBehavior
    {
        public static readonly AttachedProperty<bool> AppendFirstPathProperty =
            AvaloniaProperty.RegisterAttached<TextBoxDropPathBehavior, TextBox, bool>(
                "AppendFirstPath");

        static TextBoxDropPathBehavior()
        {
            AppendFirstPathProperty.Changed.AddClassHandler<TextBox>(OnAppendFirstPathChanged);
        }

        private TextBoxDropPathBehavior()
        {
        }

        public static bool GetAppendFirstPath(TextBox textBox)
        {
            return textBox.GetValue(AppendFirstPathProperty);
        }

        public static void SetAppendFirstPath(TextBox textBox, bool value)
        {
            textBox.SetValue(AppendFirstPathProperty, value);
        }

        private static void OnAppendFirstPathChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.GetNewValue<bool>())
            {
                DragDrop.SetAllowDrop(textBox, true);
                DragDrop.AddDragOverHandler(textBox, OnDragOver);
                DragDrop.AddDropHandler(textBox, OnDrop);
            }
            else
            {
                DragDrop.RemoveDragOverHandler(textBox, OnDragOver);
                DragDrop.RemoveDropHandler(textBox, OnDrop);
                DragDrop.SetAllowDrop(textBox, false);
            }
        }

        private static void OnDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = GetFirstDroppedPath(e) is null
                ? DragDropEffects.None
                : DragDropEffects.Copy;
            e.Handled = true;
        }

        private static void OnDrop(object? sender, DragEventArgs e)
        {
            if (sender is not TextBox textBox)
            {
                return;
            }

            var path = GetFirstDroppedPath(e);
            if (path is null)
            {
                return;
            }

            textBox.Text = AppendPath(textBox.Text, path);
            e.Handled = true;
        }

        private static string? GetFirstDroppedPath(DragEventArgs e)
        {
            return e.DataTransfer.TryGetFiles()?
                .Select(GetLocalPath)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
                ?.Trim();
        }

        private static string? GetLocalPath(IStorageItem storageItem)
        {
            return storageItem.Path.IsFile
                ? storageItem.Path.LocalPath
                : storageItem.Path.ToString();
        }

        private static string AppendPath(string? text, string path)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return path;
            }

            return text.EndsWith(Environment.NewLine, StringComparison.Ordinal)
                ? text + path
                : text + Environment.NewLine + path;
        }
    }
}
