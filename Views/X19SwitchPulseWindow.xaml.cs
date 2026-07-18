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
    public partial class X19SwitchPulseWindow : Window
    {
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpNoOwnerZOrder = 0x0200;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private readonly DispatcherTimer _closeTimer;
        private bool _canClose;

        public X19SwitchPulseWindow()
        {
            InitializeComponent();

            _closeTimer =
                new DispatcherTimer();

            _closeTimer.Tick +=
                CloseTimer_Tick;
        }

        public void ShowOverGame(
            IntPtr gameWindowHandle)
        {
            if (gameWindowHandle == IntPtr.Zero ||
                !GetWindowRect(
                    gameWindowHandle,
                    out NativeRect gameRect))
            {
                Rect workArea =
                    SystemParameters.WorkArea;

                Left =
                    workArea.Right - Width - 22;

                Top =
                    workArea.Bottom - Height - 22;

                Topmost = true;
                Show();
                BeginPulse();
                return;
            }

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
                (int)Math.Round(Width * scale);

            int overlayHeight =
                (int)Math.Round(Height * scale);

            int margin =
                (int)Math.Round(22 * scale);

            SetWindowPos(
                helper.Handle,
                IntPtr.Zero,
                Math.Max(
                    gameRect.Left,
                    gameRect.Right - overlayWidth - margin),
                Math.Max(
                    gameRect.Top,
                    gameRect.Bottom - overlayHeight - margin),
                overlayWidth,
                overlayHeight,
                SwpNoActivate |
                SwpNoOwnerZOrder);

            BeginPulse();
        }

        public void Report(
            int progress)
        {
            // I keep X19 deliberately quiet. (haha) The pulse confirms that a switch
            // is moving without covering the game with phase text.
            Opacity =
                0.48 +
                Math.Clamp(progress, 0, 100) / 250d;
        }

        public void ShowSuccess()
        {
            Brush cyan =
                (Brush)FindResource("CyanBrush");

            PulseRing.Stroke = cyan;
            IconShell.Stroke = cyan;
            IconText.Foreground = cyan;

            BeginClosingDelay(
                TimeSpan.FromMilliseconds(900));
        }

        public void ShowError()
        {
            Brush pink =
                (Brush)FindResource("PinkBrush");

            PulseRing.Stroke = pink;
            IconShell.Stroke = pink;
            IconText.Foreground = pink;

            BeginClosingDelay(
                TimeSpan.FromMilliseconds(1800));
        }

        public void CloseWhenFinished()
        {
            _canClose = true;
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

            _closeTimer.Stop();
            base.OnClosing(e);
        }

        private void BeginPulse()
        {
            Opacity = 0;

            BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(
                    0,
                    0.72,
                    TimeSpan.FromMilliseconds(180)));

            DoubleAnimation scaleAnimation =
                new()
                {
                    From = 0.82,
                    To = 1.12,
                    Duration =
                        TimeSpan.FromMilliseconds(720),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction =
                        new SineEase
                        {
                            EasingMode = EasingMode.EaseInOut
                        }
                };

            PulseScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                scaleAnimation);

            PulseScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                scaleAnimation);
        }

        private void BeginClosingDelay(
            TimeSpan delay)
        {
            _closeTimer.Stop();
            _closeTimer.Interval = delay;
            _closeTimer.Start();
        }

        private void CloseTimer_Tick(
            object? sender,
            EventArgs e)
        {
            _closeTimer.Stop();

            BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(
                    Opacity,
                    0,
                    TimeSpan.FromMilliseconds(180)));

            DispatcherTimer finishTimer =
                new()
                {
                    Interval =
                        TimeSpan.FromMilliseconds(190)
                };

            finishTimer.Tick +=
                (_, _) =>
                {
                    finishTimer.Stop();
                    CloseWhenFinished();
                };

            finishTimer.Start();
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(
            IntPtr windowHandle,
            out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(
            IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }
}
