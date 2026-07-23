using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Limelight.Views;

namespace Limelight
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly TimeSpan MinimumSplashTime =
            TimeSpan.FromSeconds(10);

        protected override async void OnStartup(
            StartupEventArgs e)
        {
            base.OnStartup(e);

            // The splash owns startup until the main window is ready.
            // Explicit shutdown keeps WPF alive between the two windows.
            ShutdownMode =
                ShutdownMode.OnExplicitShutdown;

            var splash =
                new StartupSplashWindow();

            var startupTimer =
                Stopwatch.StartNew();

            try
            {
                splash.Show();

                // I let WPF paint the splash before constructing the
                // full manager and all of its pages.
                await System.Windows.Threading.Dispatcher.Yield(
                    DispatcherPriority.Loaded);

                var mainWindow =
                    new MainWindow();

                TimeSpan remainingTime =
                    MinimumSplashTime -
                    startupTimer.Elapsed;

                if (remainingTime > TimeSpan.Zero)
                {
                    await Task.Delay(
                        remainingTime);
                }

                await splash.FadeOutAsync();
                splash.Close();

                MainWindow =
                    mainWindow;

                ShutdownMode =
                    ShutdownMode.OnMainWindowClose;

                mainWindow.Show();
            }
            catch (Exception exception)
            {
                splash.Close();

                MessageBox.Show(
                    "Limelight could not finish starting.\n\n" +
                    exception.Message,
                    "Limelight startup failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown(1);
            }
        }
    }
}
