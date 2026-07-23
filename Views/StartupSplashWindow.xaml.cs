using System.Reflection;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Limelight.Views
{
    public partial class StartupSplashWindow : Window
    {
        private readonly DispatcherTimer _stageTimer;
        private readonly string[] _loadingStages =
        {
            "PREPARING THE SPOTLIGHT",
            "LOADING YOUR MOD LIBRARY",
            "CHECKING THE LIVE LOADER",
            "WARMING UP THE STAGE",
            "READY FOR THE NEXT ACT"
        };

        private int _stageIndex;

        public StartupSplashWindow()
        {
            InitializeComponent();

            VersionText.Text =
                $"VERSION {ReadVersion()}";

            _stageTimer =
                new DispatcherTimer
                {
                    Interval =
                        TimeSpan.FromSeconds(1.9)
                };

            _stageTimer.Tick +=
                StageTimer_Tick;

            Loaded +=
                StartupSplashWindow_Loaded;

            Closed +=
                StartupSplashWindow_Closed;
        }

        private void StartupSplashWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            BeginStoryboard(
                (Storyboard)Resources[
                    "LogoPulseStoryboard"]);

            BeginStoryboard(
                (Storyboard)Resources[
                    "LoadingProgressStoryboard"]);

            BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(
                    0,
                    1,
                    TimeSpan.FromMilliseconds(320)));

            _stageTimer.Start();
        }

        private void StageTimer_Tick(
            object? sender,
            EventArgs e)
        {
            if (_stageIndex >=
                _loadingStages.Length - 1)
            {
                _stageTimer.Stop();
                return;
            }

            _stageIndex++;

            LoadingStageText.Text =
                _loadingStages[_stageIndex];
        }

        public async Task FadeOutAsync()
        {
            var finished =
                new TaskCompletionSource();

            var fade =
                new DoubleAnimation(
                    Opacity,
                    0,
                    TimeSpan.FromMilliseconds(280))
                {
                    EasingFunction =
                        new QuadraticEase
                        {
                            EasingMode =
                                EasingMode.EaseIn
                        }
                };

            fade.Completed +=
                (_, _) => finished.TrySetResult();

            BeginAnimation(
                OpacityProperty,
                fade);

            await finished.Task;
        }

        private void StartupSplashWindow_Closed(
            object? sender,
            EventArgs e)
        {
            _stageTimer.Stop();
        }

        private static string ReadVersion()
        {
            Assembly assembly =
                Assembly.GetExecutingAssembly();

            string? informationalVersion =
                assembly
                    .GetCustomAttribute<
                        AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(
                    informationalVersion))
            {
                int metadataStart =
                    informationalVersion.IndexOf(
                        '+');

                return metadataStart >= 0
                    ? informationalVersion[..metadataStart]
                    : informationalVersion;
            }

            return assembly
                       .GetName()
                       .Version?
                       .ToString() ??
                   "DEVELOPMENT";
        }
    }
}
