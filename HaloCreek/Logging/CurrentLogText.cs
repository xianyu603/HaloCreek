using System;
using System.Text;

namespace HaloCreek.Logging
{
    public static class CurrentLogText
    {
        public const int MaxTextLength = 200 * 1024;

        private static readonly object BufferLock = new();
        private static readonly StringBuilder Buffer = new();

        public static event Action<string>? TextChanged;

        public static string GetSnapshotText()
        {
            lock (BufferLock)
            {
                return Buffer.ToString();
            }
        }

        public static void AppendLine(string formattedText)
        {
            ArgumentNullException.ThrowIfNull(formattedText);

            string snapshot;
            lock (BufferLock)
            {
                Buffer.Append(formattedText);
                Buffer.AppendLine();
                TrimToCapacity();
                snapshot = Buffer.ToString();
            }

            PublishTextChanged(snapshot);
        }

        public static void Clear()
        {
            lock (BufferLock)
            {
                Buffer.Clear();
            }

            PublishTextChanged(string.Empty);
        }

        private static void TrimToCapacity()
        {
            var overflow = Buffer.Length - MaxTextLength;
            if (overflow <= 0)
            {
                return;
            }

            var trimLength = overflow;
            for (var i = overflow; i < Buffer.Length; i++)
            {
                if (Buffer[i] == '\n')
                {
                    var lineBoundaryTrimLength = i + 1;
                    if (lineBoundaryTrimLength < Buffer.Length)
                    {
                        trimLength = lineBoundaryTrimLength;
                    }

                    break;
                }
            }

            Buffer.Remove(0, trimLength);
        }

        private static void PublishTextChanged(string snapshot)
        {
            var handler = TextChanged;
            if (handler is null)
            {
                return;
            }

            foreach (var subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((Action<string>)subscriber)(snapshot);
                }
                catch
                {
                    // Log UI subscribers must not affect the logging path.
                }
            }
        }
    }
}
