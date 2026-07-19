using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Limelight.Views
{
    public partial class LiveLoaderInitializingWindow : Window
    {
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpNoOwnerZOrder = 0x0200;
        private const int SwRestore = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public LiveLoaderInitializingWindow()
        {
            InitializeComponent();
        }

        public void ShowOverGame(
            IntPtr gameWindowHandle)
        {
            if (gameWindowHandle == IntPtr.Zero ||
                !GetWindowRect(
                    gameWindowHandle,
                    out NativeRect gameRect))
            {
                // I still show the progress safely if Windows has not exposed
                // the game window yet.
                WindowStartupLocation =
                    WindowStartupLocation.CenterScreen;

                Show();
                return;
            }

            // I return focus to Dead as Disco before showing the card. This
            // makes the setup feel like part of the game's loading screen
            // instead of a dialog sitting on top of the manager.
            ShowWindowAsync(
                gameWindowHandle,
                SwRestore);

            SetForegroundWindow(
                gameWindowHandle);

            // Making the game window the native owner keeps this card above
            // Dead as Disco without placing it above every other application.
            WindowInteropHelper helper =
                new WindowInteropHelper(this)
                {
                    Owner = gameWindowHandle
                };

            Show();

            uint gameDpi =
                GetDpiForWindow(gameWindowHandle);

            double scale =
                gameDpi == 0
                    ? 1.0
                    : gameDpi / 96.0;

            int overlayWidth =
                (int)Math.Round(Width * scale);

            int overlayHeight =
                (int)Math.Round(Height * scale);

            int gameWidth =
                gameRect.Right - gameRect.Left;

            int gameHeight =
                gameRect.Bottom - gameRect.Top;

            int overlayLeft =
                gameRect.Left +
                Math.Max(
                    0,
                    (gameWidth - overlayWidth) / 2);

            int overlayTop =
                gameRect.Top +
                Math.Max(
                    0,
                    (gameHeight - overlayHeight) / 2);

            SetWindowPos(
                helper.Handle,
                IntPtr.Zero,
                overlayLeft,
                overlayTop,
                overlayWidth,
                overlayHeight,
                SwpNoActivate |
                SwpNoOwnerZOrder);
        }

        public void Report(
            string phase,
            int progress,
            string? detail = null)
        {
            PhaseText.Text = phase;

            InitialisationProgress.Value =
                Math.Clamp(
                    progress,
                    0,
                    100);

            if (!string.IsNullOrWhiteSpace(detail))
            {
                DetailText.Text = detail;
            }
        }

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(
            IntPtr windowHandle,
            out NativeRect rectangle);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(
            IntPtr windowHandle);

        [DllImport(
            "user32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(
            IntPtr windowHandle,
            int showCommand);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(
            IntPtr windowHandle);
    }
}
