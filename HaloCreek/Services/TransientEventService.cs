using System;
using Avalonia.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;

namespace HaloCreek.Services
{
    public sealed class TransientEventService
    {
        private readonly PlatformInfrastructure _platformInfrastructure;

        public TransientEventService(PlatformInfrastructure platformInfrastructure)
        {
            _platformInfrastructure = platformInfrastructure
                ?? throw new ArgumentNullException(nameof(platformInfrastructure));
        }

        public void ReportUserActionFailure(
            string category,
            string title,
            string message,
            Exception? exception = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(category);
            ArgumentException.ThrowIfNullOrWhiteSpace(title);
            ArgumentException.ThrowIfNullOrWhiteSpace(message);

            if (exception is null)
            {
                Log.Error(category, $"{title}: {message}");
            }
            else
            {
                Log.Error(category, exception, $"{title}: {message}");
            }

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await _platformInfrastructure.ShowErrorDialogAsync(title, message);
                }
                catch (Exception dialogException)
                {
                    Log.Error(category, dialogException, $"Failed to show error dialog: {title}");
                }
            });
        }
    }
}
