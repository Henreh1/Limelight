using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Limelight.Views
{
    public partial class LiveModSwitchingWindow : Window
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

        private readonly DispatcherTimer _resultTimer;
        private readonly DispatcherTimer _etaTimer;
        private readonly double _timingScale;
        private DateTime _phaseStartedAt;
        private TimeSpan _remainingAtPhaseStart;
        private bool _canClose;
        private bool _resultShown;

        public LiveModSwitchingWindow(
            string modName,
            bool isFirstLiveSwitch)
        {
            InitializeComponent();

            ModNameText.Text =
                modName.ToUpperInvariant();

            DetailText.Text =
                isFirstLiveSwitch
                    ? "The first live switch may take a little longer while Limelight prepares the package."
                    : "Limelight is preparing the selected mod for a safe live change.";

            _resultTimer =
                new DispatcherTimer();

            _timingScale =
    isFirstLiveSwitch
        ? 1.0
        : 0.55;

            _etaTimer =
                new DispatcherTimer
                {
                    Interval =
                        TimeSpan.FromMilliseconds(
                            500)
                };

            _etaTimer.Tick +=
                EtaTimer_Tick;

            _etaTimer.Start();

            _resultTimer.Tick +=
                ResultTimer_Tick;

            Loaded +=
                (_, _) =>
                    StartProgressAnimation();

            Report(
                "CHECKING MOD PACKAGE",
                12);
        }

        public void ShowOverGame(
            IntPtr gameWindowHandle)
        {
            if (gameWindowHandle == IntPtr.Zero ||
                !GetWindowRect(
                    gameWindowHandle,
                    out NativeRect gameRect))
            {
                // I use the desktop corner only when Windows has not exposed
                // the game window yet.
                Rect workArea =
                    SystemParameters.WorkArea;

                Left =
                    workArea.Right -
                    Width -
                    24;

                Top =
                    workArea.Bottom -
                    Height -
                    24;

                Topmost = true;

                Show();
                AnimateIn();
                return;
            }

            // The first preparation pass can briefly hold the game thread. I
            // bring the game forward first so its progress card is visible
            // instead of leaving the user looking at Limelight.
            ShowWindowAsync(
                gameWindowHandle,
                SwRestore);

            SetForegroundWindow(
                gameWindowHandle);

            // I make Dead as Disco the native owner so this card stays above
            // the game without covering unrelated applications.
            WindowInteropHelper helper =
                new(this)
                {
                    Owner = gameWindowHandle
                };

            Show();

            uint gameDpi =
                GetDpiForWindow(
                    gameWindowHandle);

            double scale =
                gameDpi == 0
                    ? 1.0
                    : gameDpi / 96.0;

            int overlayWidth =
                (int)Math.Round(
                    Width * scale);

            int overlayHeight =
                (int)Math.Round(
                    Height * scale);

            int margin =
                (int)Math.Round(
                    24 * scale);

            int overlayLeft =
                Math.Max(
                    gameRect.Left,
                    gameRect.Right -
                    overlayWidth -
                    margin);

            int overlayTop =
                Math.Max(
                    gameRect.Top,
                    gameRect.Bottom -
                    overlayHeight -
                    margin);

            SetWindowPos(
                helper.Handle,
                IntPtr.Zero,
                overlayLeft,
                overlayTop,
                overlayWidth,
                overlayHeight,
                SwpNoActivate |
                SwpNoOwnerZOrder);

            AnimateIn();
        }

        public void Report(
            string phase,
            int progress)
        {
            if (_resultShown)
            {
                return;
            }

            int safeProgress =
                Math.Clamp(
                    progress,
                    0,
                    100);

            PhaseText.Text =
                phase;

            DetailText.Text =
                GetPhaseDetail(
                    phase);

            ProgressText.Text =
                "WORKING";

            _phaseStartedAt =
    DateTime.UtcNow;

            _remainingAtPhaseStart =
                TimeSpan.FromSeconds(
                    GetEstimatedRemainingSeconds(
                        safeProgress) *
                    _timingScale);

            UpdateEta();

        }

        public void ShowSuccess(
            string message)
        {
            _resultShown = true;

            _etaTimer.Stop();

            EtaText.Text =
                "COMPLETE";

            Brush cyanBrush =
                (Brush)FindResource(
                    "CyanBrush");

            EtaText.Foreground =
    cyanBrush;

            PhaseText.Text =
                "LIVE SWITCH COMPLETE";

            DetailText.Text =
                message;

            ProgressText.Text =
                "DONE";

            StatusIcon.Text =
                "✓";

            PhaseText.Foreground =
                cyanBrush;

            ProgressText.Foreground =
                cyanBrush;

            StatusIcon.Foreground =
                cyanBrush;

            CompleteProgressAnimation(
                cyanBrush);

            PopupShell.BorderBrush =
                cyanBrush;

            StartResultTimer(
                TimeSpan.FromSeconds(
                    2.8));
        }

        public void ShowError(
            string message)
        {
            _resultShown = true;

            Brush pinkBrush =
                (Brush)FindResource(
                    "PinkBrush");

            EtaText.Foreground =
    pinkBrush;

            _etaTimer.Stop();

            EtaText.Text =
                "CHECK LIMELIGHT";

            PhaseText.Text =
                "LIVE SWITCH FAILED";

            DetailText.Text =
                message;

            ProgressText.Text =
                "ERROR";

            StatusIcon.Text =
                "!";

            PhaseText.Foreground =
                pinkBrush;

            ProgressText.Foreground =
                pinkBrush;

            StatusIcon.Foreground =
                pinkBrush;

            CompleteProgressAnimation(
                pinkBrush);

            PopupShell.BorderBrush =
                pinkBrush;

            StartResultTimer(
                TimeSpan.FromSeconds(
                    5.5));
        }

        public void CloseWhenFinished()
        {
            _canClose = true;
            _resultTimer.Stop();
            _etaTimer.Stop();
            SwitchProgressTransform.BeginAnimation(
                TranslateTransform.XProperty,
                null);
            Close();
        }

        protected override void OnClosing(
            CancelEventArgs e)
        {
            if (!_canClose)
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(
            EventArgs e)
        {
            _resultTimer.Stop();
            _etaTimer.Stop();

            base.OnClosed(e);
        }

        private void AnimateIn()
        {
            CubicEase easing =
                new()
                {
                    EasingMode =
                        EasingMode.EaseOut
                };

            PopupShell.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration =
                        TimeSpan.FromMilliseconds(
                            220),
                    EasingFunction = easing
                });

            PopupTransform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation
                {
                    From = 26,
                    To = 0,
                    Duration =
                        TimeSpan.FromMilliseconds(
                            260),
                    EasingFunction = easing
                });
        }

        private void StartProgressAnimation()
        {
            if (_resultShown)
            {
                return;
            }

            double trackWidth =
                SwitchProgressTrack.ActualWidth;

            if (trackWidth <= 0)
            {
                return;
            }

            double indicatorWidth =
                Math.Clamp(
                    trackWidth * 0.28,
                    82,
                    118);

            SwitchProgressIndicator.Width =
                indicatorWidth;

            SwitchProgressIndicator.Background =
                new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new(
                            Color.FromArgb(
                                0,
                                57,
                                221,
                                245),
                            0),
                        new(
                            Color.FromRgb(
                                57,
                                221,
                                245),
                            0.45),
                        new(
                            Color.FromArgb(
                                0,
                                57,
                                221,
                                245),
                            1)
                    },
                    new Point(0, 0.5),
                    new Point(1, 0.5));

            // This light never claims to know Unreal's exact percentage. It
            // simply keeps moving while the current operation is alive.
            SwitchProgressTransform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation
                {
                    From = -indicatorWidth,
                    To = trackWidth,
                    Duration =
                        TimeSpan.FromMilliseconds(
                            1150),
                    RepeatBehavior =
                        RepeatBehavior.Forever
                });
        }

        private void CompleteProgressAnimation(
            Brush resultBrush)
        {
            SwitchProgressTransform.BeginAnimation(
                TranslateTransform.XProperty,
                null);

            SwitchProgressTransform.X = 0;
            SwitchProgressIndicator.Width =
                SwitchProgressTrack.ActualWidth;
            SwitchProgressIndicator.Background =
                resultBrush;
        }

        private void StartResultTimer(
            TimeSpan delay)
        {
            _resultTimer.Stop();
            _resultTimer.Interval = delay;
            _resultTimer.Start();
        }

        private void ResultTimer_Tick(
            object? sender,
            EventArgs e)
        {
            _resultTimer.Stop();

            CubicEase easing =
                new()
                {
                    EasingMode =
                        EasingMode.EaseIn
                };

            DoubleAnimation opacityAnimation =
                new()
                {
                    To = 0,
                    Duration =
                        TimeSpan.FromMilliseconds(
                            180),
                    EasingFunction = easing
                };

            opacityAnimation.Completed +=
                (_, _) =>
                {
                    _canClose = true;
                    Close();
                };

            PopupShell.BeginAnimation(
                OpacityProperty,
                opacityAnimation);

            PopupTransform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation
                {
                    To = 22,
                    Duration =
                        TimeSpan.FromMilliseconds(
                            180),
                    EasingFunction = easing
                });
        }

        private static string GetPhaseDetail(
            string phase)
        {
            return phase switch
            {
                "SCANNING MOD CONTENT" =>
                    "Reading every replacement contained in the selected mod.",

                "STAGING MOD CONTAINER" =>
                    "Preparing the package for this live game session.",

                "MOUNTING MOD CONTENT" =>
                    "Unreal is mounting the replacement content.",

                "REFRESHING OVERRIDDEN PACKAGES" =>
                    "Clearing cached packages before loading their replacements.",

                "LOADING MODELS, PORTRAITS AND TEXT" =>
                    "Loading the selected model, materials, portraits, and text.",

                "LIVE LOADER READY" =>
                    "The new character and supporting assets are ready.",

                "FINALISING THE PREVIOUS MODEL" =>
                    "Unreal is releasing the previous character before another switch is allowed.",

                _ =>
                    "Limelight is confirming that Unreal is ready for a safe live switch."
            };
        }

        private void EtaTimer_Tick(
    object? sender,
    EventArgs e)
        {
            UpdateEta();
        }

        private void UpdateEta()
        {
            TimeSpan elapsed =
                DateTime.UtcNow -
                _phaseStartedAt;

            TimeSpan remaining =
                _remainingAtPhaseStart -
                elapsed;

            EtaText.Text =
                FormatRemainingTime(
                    remaining);
        }

        private static double GetEstimatedRemainingSeconds(
            int progress)
        {
            if (progress <= 12)
            {
                return 225;
            }

            if (progress <= 35)
            {
                return 210;
            }

            if (progress <= 48)
            {
                return 190;
            }

            if (progress <= 60)
            {
                return 165;
            }

            if (progress <= 74)
            {
                return 40;
            }

            if (progress <= 86)
            {
                return 25;
            }

            return 8;
        }

        private static string FormatRemainingTime(
            TimeSpan remaining)
        {
            if (remaining <= TimeSpan.Zero)
            {
                return "FINISHING UP";
            }

            int totalSeconds =
                (int)Math.Ceiling(
                    remaining.TotalSeconds);

            if (totalSeconds < 60)
            {
                return $"ABOUT {totalSeconds} SEC";
            }

            int minutes =
                totalSeconds / 60;

            int seconds =
                totalSeconds % 60;

            return $"ABOUT {minutes}:{seconds:00}";
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
