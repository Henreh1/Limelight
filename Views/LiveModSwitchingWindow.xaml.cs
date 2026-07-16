using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Limelight.Views
{
    public partial class LiveModSwitchingWindow : Window
    {
        private readonly DispatcherTimer _animationTimer;
        private readonly double _timingScale;
        private DateTime _phaseStartedAt;
        private double _phaseStartProgress;
        private double _phaseCeilingProgress;
        private TimeSpan _phaseDuration;
        private TimeSpan _remainingAtPhaseStart;
        private bool _canClose;

        public LiveModSwitchingWindow(
            string modName,
            bool isFirstLiveSwitch)
        {
            InitializeComponent();

            ModNameText.Text =
                $"PREPARING {modName.ToUpperInvariant()}";

            _timingScale =
                isFirstLiveSwitch
                    ? 1.0
                    : 0.55;

            TimingNoteText.Text =
                isFirstLiveSwitch
                    ? "The first live switch performs the full package scan and normally takes the longest."
                    : "This package was already prepared once, so the estimated time is usually shorter.";

            _animationTimer =
                new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(250)
                };

            _animationTimer.Tick +=
                AnimationTimer_Tick;

            Report(
                "CHECKING MOD PACKAGE",
                12);
        }

        public void Report(
            string phase,
            int progress)
        {
            int safeProgress =
                Math.Clamp(
                    progress,
                    0,
                    100);

            PhaseText.Text = phase;
            DetailText.Text = GetPhaseDetail(phase);

            if (safeProgress >= 100)
            {
                _animationTimer.Stop();
                SwitchProgress.Value = 100;
                ProgressText.Text = "100%";
                EtaText.Text = "READY";
                return;
            }

            (double ceiling,
             double phaseSeconds,
             double remainingSeconds) =
                GetPhaseTiming(safeProgress);

            _phaseStartProgress = safeProgress;
            _phaseCeilingProgress = ceiling;
            _phaseDuration =
                TimeSpan.FromSeconds(
                    Math.Max(
                        1,
                        phaseSeconds * _timingScale));

            _remainingAtPhaseStart =
                TimeSpan.FromSeconds(
                    Math.Max(
                        1,
                        remainingSeconds * _timingScale));

            _phaseStartedAt = DateTime.UtcNow;
            SwitchProgress.Value = safeProgress;

            UpdateAnimatedValues();
            _animationTimer.Start();
        }

        public void CloseWhenFinished()
        {
            _canClose = true;
            _animationTimer.Stop();
            Close();
        }

        protected override void OnClosing(
            CancelEventArgs e)
        {
            if (!_canClose)
            {
                // Limelight owns the operation that opened this window. Keeping
                // it visible prevents another activation while files are moving.
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }

        private void AnimationTimer_Tick(
            object? sender,
            EventArgs e)
        {
            UpdateAnimatedValues();
        }

        private void UpdateAnimatedValues()
        {
            TimeSpan elapsed =
                DateTime.UtcNow -
                _phaseStartedAt;

            double phaseFraction =
                Math.Min(
                    0.92,
                    elapsed.TotalMilliseconds /
                    Math.Max(
                        1,
                        _phaseDuration.TotalMilliseconds));

            double animatedProgress =
                _phaseStartProgress +
                ((_phaseCeilingProgress -
                  _phaseStartProgress) *
                 phaseFraction);

            SwitchProgress.Value =
                Math.Max(
                    SwitchProgress.Value,
                    animatedProgress);

            ProgressText.Text =
                $"{Math.Floor(SwitchProgress.Value)}%";

            TimeSpan remaining =
                _remainingAtPhaseStart -
                elapsed;

            EtaText.Text =
                FormatRemainingTime(remaining);
        }

        private static string GetPhaseDetail(
            string phase)
        {
            return phase switch
            {
                "SCANNING MOD CONTENT" =>
                    "Reading the mod container and building a list of every replacement asset.",
                "STAGING MOD CONTAINER" =>
                    "Preparing a uniquely named live container for this game session.",
                "MOUNTING MOD CONTENT" =>
                    "Unreal is mounting the package. The first mount normally takes the longest.",
                "REFRESHING OVERRIDDEN PACKAGES" =>
                    "Removing cached base game packages so the mounted replacements can take their place.",
                "LOADING MODELS, PORTRAITS AND TEXT" =>
                    "Loading registered replacements first. New materials and textures will follow the character package.",
                "LIVE LOADER READY" =>
                    "The selected mod is mounted and Charlie has been refreshed.",
                _ =>
                    "Limelight is confirming that Unreal is ready for a safe live change."
            };
        }

        private static (double Ceiling,
                        double PhaseSeconds,
                        double RemainingSeconds)
            GetPhaseTiming(
                int progress)
        {
            if (progress <= 12)
            {
                return (34, 15, 225);
            }

            if (progress <= 35)
            {
                return (47, 20, 210);
            }

            if (progress <= 48)
            {
                return (59, 25, 190);
            }

            if (progress <= 60)
            {
                return (73, 125, 165);
            }

            if (progress <= 74)
            {
                return (85, 15, 40);
            }

            return (99, 25, 25);
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

            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;

            return $"ABOUT {minutes}:{seconds:00}";
        }
    }
}
