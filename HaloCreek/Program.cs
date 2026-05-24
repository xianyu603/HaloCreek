using Avalonia;
using System;
using HaloCreek.Logging;

namespace HaloCreek
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            Log.InitializeFileOutput();

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                Log.Info("Application", "HaloCreek closed.");
            }
            catch (Exception ex)
            {
                Log.Error("Application", ex, "HaloCreek terminated with an unhandled exception.");
                throw;
            }
            finally
            {
                Log.Shutdown();
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}
