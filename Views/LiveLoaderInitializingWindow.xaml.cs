using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Limelight.Views
{
    public partial class LiveLoaderInitializingWindow : Window
    {
        private const uint SwpNoOwnerZOrder = 0x0200;
        private const int SwRestore = 9;

        private readonly DispatcherTimer _etaTimer;
        private IntPtr _gameWindowHandle;
        private DateTime _phaseStartedAt;
        private TimeSpan _phaseEstimate;
        private string _estimatedPhase =
            string.Empty;

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

            _etaTimer =
                new DispatcherTimer
                {
                    Interval =
                        TimeSpan.FromMilliseconds(500)
                };

            _etaTimer.Tick +=
                (_, _) => UpdateEta();

            Loaded +=
                (_, _) =>
                {
                    StartProgressAnimation();
                    _etaTimer.Start();
                    UpdateEta();
                };

            Closed +=
                (_, _) =>
                {
                    _etaTimer.Stop();
                    InitialisationProgressTransform.BeginAnimation(
                        System.Windows.Media.TranslateTransform.XProperty,
                        null);

                    ReturnControlToGame();
                };

            BeginPhaseEstimate(
                "WAITING FOR DEAD AS DISCO");
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

            _gameWindowHandle =
                gameWindowHandle;

            // I restore the game window without activating it. Unreal keeps
            // loading underneath while this card temporarily owns input.
            ShowWindowAsync(
                gameWindowHandle,
                SwRestore);

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
                SwpNoOwnerZOrder);

            // The scan can take a while on its first run. Keeping focus here
            // prevents an early movement or controller press from reaching
            // the game before Limelight has completed its final safety check.
            Activate();
            Focus();
        }

        private void ReturnControlToGame()
        {
            if (_gameWindowHandle == IntPtr.Zero)
            {
                return;
            }

            IntPtr gameWindowHandle =
                _gameWindowHandle;

            _gameWindowHandle =
                IntPtr.Zero;

            // Closing the card is the handoff point. The game has remained
            // running throughout the scan and can now receive player input.
            ShowWindowAsync(
                gameWindowHandle,
                SwRestore);

            SetForegroundWindow(
                gameWindowHandle);
        }

        public void Report(
            string phase,
            int progress,
            string? detail = null)
        {
            PhaseText.Text = phase;

            BeginPhaseEstimate(phase);

            if (!string.IsNullOrWhiteSpace(detail))
            {
                DetailText.Text = detail;
            }
        }

        private void StartProgressAnimation()
        {
            double trackWidth =
                InitialisationProgressTrack.ActualWidth;

            if (trackWidth <= 0)
            {
                return;
            }

            double indicatorWidth =
                Math.Clamp(
                    trackWidth * 0.28,
                    92,
                    132);

            InitialisationProgressIndicator.Width =
                indicatorWidth;

            // I keep this light moving while Limelight is waiting on Unreal.
            // It shows activity without claiming a false exact percentage.
            InitialisationProgressTransform.BeginAnimation(
                System.Windows.Media.TranslateTransform.XProperty,
                new DoubleAnimation
                {
                    From = -indicatorWidth,
                    To = trackWidth,
                    Duration =
                        TimeSpan.FromMilliseconds(1150),
                    RepeatBehavior =
                        RepeatBehavior.Forever
                });
        }

        private void BeginPhaseEstimate(
            string phase)
        {
            if (string.Equals(
                    _estimatedPhase,
                    phase,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _estimatedPhase =
                phase;

            _phaseStartedAt =
                DateTime.UtcNow;

            _phaseEstimate =
                TimeSpan.FromSeconds(
                    GetEstimatedPhaseSeconds(phase));

            UpdateEta();
        }

        private void UpdateEta()
        {
            if (EtaText is null)
            {
                return;
            }

            TimeSpan remaining =
                _phaseEstimate -
                (DateTime.UtcNow - _phaseStartedAt);

            EtaText.Text =
                FormatRemainingTime(remaining);
        }

        private static int GetEstimatedPhaseSeconds(
            string phase)
        {
            string normalizedPhase =
                phase.ToUpperInvariant();

            if (normalizedPhase.Contains("MOUNT BRIDGE"))
            {
                return 150;
            }

            if (normalizedPhase.Contains("CONNECTING") ||
                normalizedPhase.Contains("WAITING FOR DEAD"))
            {
                return 90;
            }

            if (normalizedPhase.Contains("FIRST LEVEL") ||
                normalizedPhase.Contains("FIRST GAME WORLD"))
            {
                return 45;
            }

            if (normalizedPhase.Contains("MOUNTING"))
            {
                return 35;
            }

            if (normalizedPhase.Contains("LOADING MODELS") ||
                normalizedPhase.Contains("REFRESHING"))
            {
                return 25;
            }

            return 20;
        }

        private static string FormatRemainingTime(
            TimeSpan remaining)
        {
            if (remaining <= TimeSpan.Zero)
            {
                return "STILL WORKING";
            }

            int totalSeconds =
                (int)Math.Ceiling(
                    remaining.TotalSeconds);

            if (totalSeconds < 60)
            {
                return $"ABOUT {totalSeconds} SEC";
            }

            return
                $"ABOUT {totalSeconds / 60}:{totalSeconds % 60:00}";
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
