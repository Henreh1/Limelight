using Limelight.Models;
using Limelight.Services;
using Limelight.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Diagnostics;

namespace Limelight
{
    public partial class MainWindow : Window
    {
        private enum NavigationPage
        {
            Dashboard,
            MyMods,
            Profiles,
            LiveLoaders,
            BrowseNexus,
            Downloads,
            Settings
        }

        private sealed class TutorialStep
        {
            public TutorialStep(
                NavigationPage page,
                FrameworkElement target,
                string eyebrow,
                string title,
                string description,
                string hint)
            {
                Page = page;
                Target = target;
                Eyebrow = eyebrow;
                Title = title;
                Description = description;
                Hint = hint;
            }

            public NavigationPage Page { get; }
            public FrameworkElement Target { get; }
            public string Eyebrow { get; }
            public string Title { get; }
            public string Description { get; }
            public string Hint { get; }
        }

        private const int CurrentTutorialVersion = 1;

        private readonly SettingsService _settingsService;
        private readonly ModLibraryService _modLibraryService;
        private readonly AppSettings _settings;
        private readonly ModDeploymentService _modDeploymentService;
        private readonly ExistingModsMigrationService _existingModsMigrationService;
        private readonly GameProcessService _gameProcessService;
        private readonly GlobalHotkeyService _globalHotkeyService;
        private readonly Ue4ssDetectionService _ue4ssDetectionService;
        private readonly Ue4ssReleaseService _ue4ssReleaseService;
        private readonly Ue4ssInstallerService _ue4ssInstallerService;
        private readonly DeadAsDiscoUe4ssConfigurationService _ue4ssConfigurationService;
        private readonly LiveLoaderBridgeService _liveLoaderBridgeService;
        private readonly LiveLoaderCommandService _liveLoaderCommandService;
        private readonly LiveModStagingService _liveModStagingService;
        private readonly LiveSessionService _liveSessionService;
        private readonly NativeBridgeInstallerService _nativeBridgeInstallerService;
        private readonly CompatibilityService _compatibilityService;
        private readonly DiagnosticReportService _diagnosticReportService;
        private readonly PrivateTestReportService _privateTestReportService;
        private readonly NexusApiService _nexusApiService;
        private readonly DownloadHistoryService _downloadHistoryService;
        private readonly NexusCredentialService _nexusCredentialService;
        private readonly DiscordPresenceService _discordPresenceService;
        private ResourceUsageOverlayWindow? _resourceUsageOverlayWindow;

        private NexusAccount? _nexusAccount;

        private string _nexusApiKey =
            string.Empty;
        private readonly List<NexusModSummary> _nexusBrowseMods =
    new();

        private string _nexusSearchQuery =
            string.Empty;

        private string _nexusCategoryFilter =
            string.Empty;

        private string _discordPresenceSwitchTarget =
            string.Empty;

        private bool _isNexusBrowseLoading;
        private bool _isNexusDownloadRunning;
        private bool _hasLoadedNexusBrowseMods;
        private readonly DispatcherTimer _gameStatusTimer;
        private bool _hasHandledLiveLoaderPrompt;
        private bool _isLiveLoaderSetupRunning;
        private bool _isLiveModChangeRunning;
        private bool _isX19SwitchRequest;
        private bool _isX19SafetyProbeRunning;
        private bool _isLiveLoaderInitializationRunning;
        private bool _hasInitialisedCurrentGameSession;
        private bool _wasGameRunning;
        private bool _isApplyingPendingDeployment;
        private bool _pendingDeploymentAttempted;
        private int _nextLiveMountOrder = 1000;
        private int _notificationSequence;
        private readonly List<TutorialStep> _tutorialSteps =
            new List<TutorialStep>();
        private int _tutorialStepIndex;
        private LoaderLaunchMode _selectedLoaderMode =
            LoaderLaunchMode.Normal;
        private NavigationPage _selectedNavigationPage =
            NavigationPage.Dashboard;
        private bool _windowTransitionInProgress;
        private bool _animateWindowAfterRestore;
        private bool _isModImportInProgress;

        private string? _gameDirectory;

        public MainWindow()
        {
            InitializeComponent();

            _settingsService =
                new SettingsService();

            _modLibraryService =
                new ModLibraryService();

            _modDeploymentService =
                new ModDeploymentService();

            _existingModsMigrationService =
                new ExistingModsMigrationService();

            _gameProcessService =
                new GameProcessService();

            _globalHotkeyService =
                new GlobalHotkeyService();

            _globalHotkeyService.Pressed +=
                X19HotkeyPressed;

            _ue4ssDetectionService =
                new Ue4ssDetectionService();

            _ue4ssReleaseService =
                new Ue4ssReleaseService();

            _ue4ssInstallerService =
                new Ue4ssInstallerService();

            _ue4ssConfigurationService =
                new DeadAsDiscoUe4ssConfigurationService();

            _liveLoaderBridgeService =
                new LiveLoaderBridgeService();

            _nativeBridgeInstallerService =
                new NativeBridgeInstallerService();

            _compatibilityService =
                new CompatibilityService(
                    _ue4ssDetectionService,
                    _ue4ssConfigurationService,
                    _liveLoaderBridgeService,
                    _nativeBridgeInstallerService);

            _liveLoaderCommandService =
                new LiveLoaderCommandService();

            _liveModStagingService =
                new LiveModStagingService();

            _liveSessionService =
                new LiveSessionService();

            _diagnosticReportService =
                new DiagnosticReportService();

            _privateTestReportService =
                new PrivateTestReportService();

            _nexusApiService =
                new NexusApiService();

            _downloadHistoryService =
                new DownloadHistoryService();

            _nexusApiService.UsageChanged +=
    NexusUsageChanged;

            SettingsPageControl.ShowNexusUsage(
                _nexusApiService.UsageSnapshot);

            _nexusCredentialService =
                new NexusCredentialService();

            _settings =
                _settingsService.Load();

            _settings.ModProfiles ??=
                new List<ModProfile>();

            _settings.X19LoaderProfileIds ??=
                new List<string>();

            _discordPresenceService =
                new DiscordPresenceService();

            _discordPresenceService.SetEnabled(
                _settings.DiscordRichPresenceEnabled);

            // The page reports its button clicks to the main window, where
            // the settings and connected game directory are available.
            MyModsPageControl.ToggleModRequested +=
                ToggleModRequested;

            MyModsPageControl.RemoveModRequested +=
                RemoveModRequested;

            MyModsPageControl.RenameModRequested +=
                RenameModRequested;

            ProfilesPageControl.ProfilesChanged +=
                ProfilesChanged;

            ProfilesPageControl.UseProfileInX19Requested +=
                UseProfileInX19Requested;

            LiveLoadersPageControl.X19GroupChanged +=
                X19GroupChanged;

            LiveLoadersPageControl.X19ProfileGroupsChanged +=
                X19ProfileGroupsChanged;

            LiveLoadersPageControl.X19ShuffleChanged +=
                X19ShuffleChanged;

            LiveLoadersPageControl.X19HotkeyChanged +=
                X19HotkeyChanged;

            SettingsPageControl.RepairRequested +=
                RepairLiveLoaderRequested;

            SettingsPageControl.PurgeAllModsRequested +=
                PurgeAllModsRequested;

            SettingsPageControl.ExportDiagnosticsRequested +=
                ExportDiagnosticsRequested;

            SettingsPageControl.CreatePrivateTestReportRequested +=
                CreatePrivateTestReportRequested;

            SettingsPageControl.ChangeGameFolderRequested +=
                ChangeGameFolderRequested;

            SettingsPageControl.NexusConnectRequested +=
                NexusConnectRequested;

            SettingsPageControl.NexusDisconnectRequested +=
                NexusDisconnectRequested;

            SettingsPageControl.DiscordPresenceChanged +=
                DiscordPresenceChanged;

            SettingsPageControl.ResourceOverlayChanged +=
                ResourceOverlayChanged;

            BrowseNexusPageControl.SearchRequested +=
                NexusSearchRequested;

            BrowseNexusPageControl.SortChanged +=
                NexusSortChanged;

            BrowseNexusPageControl.CategoryChanged +=
                NexusCategoryChanged;

            BrowseNexusPageControl.RefreshRequested +=
                NexusRefreshRequested;

            BrowseNexusPageControl.ViewModRequested +=
                NexusViewModRequested;

            BrowseNexusPageControl.ViewFilesRequested +=
                NexusViewFilesRequested;

            BrowseNexusPageControl.DownloadRequested +=
                NexusDownloadRequested;

            DownloadsPageControl.ClearFinishedRequested +=
                ClearFinishedDownloadsRequested;


            // Checking every two seconds keeps the display responsive without
            // constantly asking Windows for its process list.
            _gameStatusTimer =
                new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };

            _gameStatusTimer.Tick +=
                GameStatusTimer_Tick;

            RestoreSavedGameDirectory();
            RefreshLibrarySummary();
            RefreshDownloadsPage();

            // Wait until the window is visible before starting timers or
            // showing the existing-mod migration prompt.
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            SizeChanged += MainWindow_SizeChanged;
        }

        private async void MainWindow_Loaded(
    object sender,
    RoutedEventArgs e)
        {
            UpdateGameRunningStatus();

            bool isGameRunning =
                !string.IsNullOrWhiteSpace(_gameDirectory) &&
                _gameProcessService.IsGameRunning(
                    _gameDirectory);

            if (!isGameRunning &&
                !string.IsNullOrWhiteSpace(_gameDirectory))
            {
                string gameDirectory =
                    _gameDirectory;

                ClearLiveLoaderSessionBypass();

                // A previous crash can leave staged containers behind. They
                // are safe to remove once Windows confirms the game is closed.
                await Task.Run(() =>
                    _liveSessionService.RecoverClosedGame(
                        gameDirectory));
            }

            RefreshSettingsPage();
            RefreshDiscordPresence(
                isGameRunning);

            if (NexusApiService.IntegrationEnabled)
            {
                await RestoreNexusConnectionAsync();
            }
            else
            {
                // I leave the saved credential untouched while Nexus reviews
                // Limelight, but this Preview build never opens or validates it.
                SettingsPageControl.ShowNexusUnavailable();
            }

            // Finish any existing-mod migration before opening another modal window.
            await CheckForExistingMods();
            await ApplyPendingDeploymentIfPossible();
            await ShowLiveLoaderSetupPromptIfNeeded();

            _wasGameRunning =
                !string.IsNullOrWhiteSpace(_gameDirectory) &&
                _gameProcessService.IsGameRunning(
                    _gameDirectory);

            _gameStatusTimer.Start();

            if (_wasGameRunning)
            {
                _liveSessionService.EnsureSession(
                    _gameDirectory!);

                await InitialiseLiveLoaderForRunningGameAsync(
                    waitForGameProcess: false);
            }

            bool tutorialNeeded =
                _settings.CompletedTutorialVersion <
                CurrentTutorialVersion;

            ShowFirstRunTutorialIfNeeded();

            if (!tutorialNeeded)
            {
                QueueWhatsNewWindow();
            }
        }

        private void QueueWhatsNewWindow()
        {
            // I let the main window finish its first layout before opening the
            // update card. This keeps the splash and release notes separate.
            Dispatcher.BeginInvoke(
                new Action(ShowWhatsNewWindowIfNeeded),
                DispatcherPriority.ApplicationIdle);
        }

        private void ShowWhatsNewWindowIfNeeded()
        {
            if (_settings.CompletedTutorialVersion <
                    CurrentTutorialVersion ||
                TutorialOverlay.Visibility ==
                    Visibility.Visible)
            {
                return;
            }

            string version =
                GetCurrentVersion();

            if (string.Equals(
                    _settings.LastSeenReleaseNotesVersion,
                    version,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ReleaseNotesContent content =
                ReleaseNotesContent.CreateCurrent(version);

            WhatsNewWindow window =
                new WhatsNewWindow(content)
                {
                    Owner = this
                };

            window.ShowDialog();

            // Closing the card means the user has acknowledged this release.
            // I save immediately so it stays dismissed after a restart.
            _settings.LastSeenReleaseNotesVersion =
                version;

            _settingsService.Save(_settings);
        }

        private static string GetCurrentVersion()
        {
            Assembly assembly =
                typeof(MainWindow).Assembly;

            string? informationalVersion =
                assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                int metadataStart =
                    informationalVersion.IndexOf('+');

                return metadataStart >= 0
                    ? informationalVersion[..metadataStart]
                    : informationalVersion;
            }

            return assembly.GetName().Version?.ToString() ??
                "PREVIEW BUILD";
        }

        private void ShowFirstRunTutorialIfNeeded()
        {
            if (_settings.CompletedTutorialVersion >=
                CurrentTutorialVersion)
            {
                return;
            }

            _tutorialSteps.Clear();

            _tutorialSteps.AddRange(new[]
            {
                new TutorialStep(
                    NavigationPage.Dashboard,
                    DashboardNavigation,
                    "WELCOME TO LIMELIGHT",
                    "YOUR MODS. YOUR STAGE.",
                    "Limelight manages Dead as Disco character mods, launches the game, and keeps supported replacements available while the game is running.",
                    "This tour opens each real Limelight page. Nothing will be installed or changed while you look around."),
                new TutorialStep(
                    NavigationPage.Dashboard,
                    GameConnectionCard,
                    "FIRST CONNECTION",
                    "POINT LIMELIGHT AT THE GAME",
                    "Connect the Dead as Disco installation folder once. Limelight remembers it, checks the game status, and keeps all managed files in the correct locations.",
                    "You can change the connected folder later from Settings."),
                new TutorialStep(
                    NavigationPage.Dashboard,
                    ImportModButton,
                    "BUILD YOUR LIBRARY",
                    "IMPORT A MOD ARCHIVE",
                    "Import a ZIP, RAR, or 7Z mod archive and Limelight will validate it, scan its package contents, and add it to your private character library.",
                    "Limelight prevents duplicate imports and never edits the original archive."),
                new TutorialStep(
                    NavigationPage.MyMods,
                    MyModsNavigation,
                    "MY MODS",
                    "CHOOSE WHO TAKES THE SPOTLIGHT",
                    "Your installed characters live here. Activate a supported model, review its status, or remove it from Limelight when you no longer need it.",
                    "When Dead as Disco is running, Activate asks the Live Loader to switch safely."),
                new TutorialStep(
                    NavigationPage.LiveLoaders,
                    LiveLoadersNavigation,
                    "LIVE LOADERS",
                    "NORMAL OR X19 MODE",
                    "Normal mode changes characters from Limelight. X19 LLoader creates an ordered or shuffled group that can rotate from an in-game keyboard or controller shortcut.",
                    "Select the X19 group before launching the game with X19 mode."),
                new TutorialStep(
                    NavigationPage.BrowseNexus,
                    BrowseNexusNavigation,
                    "BROWSE NEXUS",
                    "CATALOGUE ACCESS IS COMING",
                    "The Nexus catalogue is temporarily paused while Limelight's application registration is reviewed. This page will unlock after approval.",
                    "For this Preview, import a mod archive from the Dashboard or drag its ZIP directly onto Limelight."),
                new TutorialStep(
                    NavigationPage.Downloads,
                    DownloadsNavigation,
                    "DOWNLOADS",
                    "FOLLOW EVERY TRANSFER",
                    "The Downloads page shows active progress, completed imports, and any failure that needs attention.",
                    "Nexus direct downloads require an eligible Nexus account."),
                new TutorialStep(
                    NavigationPage.Settings,
                    SettingsNavigation,
                    "SETTINGS AND SUPPORT",
                    "KEEP THE SHOW RUNNING",
                    "Settings contains game connection, Live Loader controls, Nexus access, Discord activity, optional resource monitoring, repair tools, and private diagnostic reports.",
                    "Diagnostic reports remove the saved Nexus key before they are created."),
                new TutorialStep(
                    NavigationPage.Dashboard,
                    LaunchGameButton,
                    "READY FOR THE SPOTLIGHT",
                    "LAUNCH WHEN YOU ARE READY",
                    "Launch Dead as Disco from here after choosing a character and loader mode. Limelight will prepare the managed bridge automatically and remain available for safe live changes.",
                    "You can replay the important pages at any time from the navigation bar.")
            });

            _tutorialStepIndex = 0;
            TutorialOverlay.Visibility =
                Visibility.Visible;

            ShowTutorialStep();
        }

        private void ShowTutorialStep()
        {
            if (_tutorialSteps.Count == 0)
            {
                return;
            }

            TutorialStep step =
                _tutorialSteps[_tutorialStepIndex];

            NavigateForTutorial(step.Page);

            TutorialEyebrowText.Text =
                step.Eyebrow;
            TutorialTitleText.Text =
                step.Title;
            TutorialDescriptionText.Text =
                step.Description;
            TutorialHintText.Text =
                step.Hint;
            TutorialStepCounterText.Text =
                $"{_tutorialStepIndex + 1} OF {_tutorialSteps.Count}";

            TutorialPreviousButton.IsEnabled =
                _tutorialStepIndex > 0;
            TutorialPreviousButton.Opacity =
                _tutorialStepIndex > 0
                    ? 1
                    : 0.45;

            TutorialNextButton.Content =
                _tutorialStepIndex ==
                _tutorialSteps.Count - 1
                    ? "FINISH TOUR"
                    : "NEXT";

            Dispatcher.BeginInvoke(
                new Action(() =>
                    PositionTutorialSpotlight(step.Target)),
                DispatcherPriority.Loaded);
        }

        private void NavigateForTutorial(
            NavigationPage page)
        {
            switch (page)
            {
                case NavigationPage.MyMods:
                    ShowMyModsPage();
                    break;

                case NavigationPage.LiveLoaders:
                    ShowLiveLoadersPage();
                    break;

                case NavigationPage.Profiles:
                    ShowProfilesPage();
                    break;

                case NavigationPage.BrowseNexus:
                    ShowBrowseNexusPage();
                    break;

                case NavigationPage.Downloads:
                    ShowDownloadsPage();
                    break;

                case NavigationPage.Settings:
                    ShowSettingsPage();
                    break;

                default:
                    ShowDashboardPage();
                    break;
            }
        }

        private void PositionTutorialSpotlight(
            FrameworkElement target)
        {
            if (TutorialOverlay.Visibility !=
                    Visibility.Visible ||
                !target.IsVisible ||
                target.ActualWidth <= 0 ||
                target.ActualHeight <= 0)
            {
                return;
            }

            try
            {
                Point targetPosition =
                    target.TransformToAncestor(
                            ApplicationContentRoot)
                        .Transform(new Point(0, 0));

                const double spotlightPadding = 7;

                Canvas.SetLeft(
                    TutorialSpotlight,
                    Math.Max(
                        0,
                        targetPosition.X - spotlightPadding));

                Canvas.SetTop(
                    TutorialSpotlight,
                    Math.Max(
                        0,
                        targetPosition.Y - spotlightPadding));

                TutorialSpotlight.Width =
                    target.ActualWidth +
                    spotlightPadding * 2;

                TutorialSpotlight.Height =
                    target.ActualHeight +
                    spotlightPadding * 2;

                // The card uses the opposite side so the highlighted control
                // remains visible instead of sitting underneath the guide.
                TutorialCard.HorizontalAlignment =
                    targetPosition.X < 300
                        ? HorizontalAlignment.Right
                        : HorizontalAlignment.Left;

                TutorialCard.VerticalAlignment =
                    targetPosition.Y <
                    ApplicationContentRoot.ActualHeight * 0.45
                        ? VerticalAlignment.Bottom
                        : VerticalAlignment.Top;
            }
            catch (InvalidOperationException)
            {
                // A page can still be completing its first layout pass. The
                // next size or tutorial step update will position the outline.
            }
        }

        private void TutorialPrevious_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_tutorialStepIndex <= 0)
            {
                return;
            }

            --_tutorialStepIndex;
            ShowTutorialStep();
        }

        private void TutorialNext_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_tutorialStepIndex <
                _tutorialSteps.Count - 1)
            {
                ++_tutorialStepIndex;
                ShowTutorialStep();
                return;
            }

            CompleteTutorial();
        }

        private void TutorialSkip_Click(
            object sender,
            RoutedEventArgs e)
        {
            CompleteTutorial();
        }

        private void CompleteTutorial()
        {
            _settings.CompletedTutorialVersion =
                CurrentTutorialVersion;

            _settingsService.Save(_settings);

            TutorialOverlay.Visibility =
                Visibility.Collapsed;

            ShowDashboardPage();

            QueueWhatsNewWindow();
        }

        private void MainWindow_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            if (TutorialOverlay.Visibility !=
                    Visibility.Visible ||
                _tutorialSteps.Count == 0)
            {
                return;
            }

            PositionTutorialSpotlight(
                _tutorialSteps[_tutorialStepIndex].Target);
        }

        private async void MinimiseWindow_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_windowTransitionInProgress)
            {
                return;
            }

            _windowTransitionInProgress =
                true;

            try
            {
                // A custom title bar does not receive Windows' full native
                // minimise animation. I soften the hand-off so it still
                // feels connected to the taskbar instead of disappearing.
                await AnimateWindowVisualAsync(
                    opacity: 0.35,
                    scale: 0.965,
                    milliseconds: 115);

                _animateWindowAfterRestore =
                    true;

                SystemCommands.MinimizeWindow(
                    this);
            }
            finally
            {
                _windowTransitionInProgress =
                    false;
            }
        }

        private async void ToggleMaximiseWindow_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_windowTransitionInProgress)
            {
                return;
            }

            _windowTransitionInProgress =
                true;

            try
            {
                await AnimateWindowVisualAsync(
                    opacity: 0.72,
                    scale: 0.985,
                    milliseconds: 90);

                if (WindowState == WindowState.Maximized)
                {
                    SystemCommands.RestoreWindow(
                        this);
                }
                else
                {
                    SystemCommands.MaximizeWindow(
                        this);
                }

                // One render pass lets Windows finish changing the outer
                // bounds before Limelight brings its contents back in.
                await Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Render);

                await AnimateWindowVisualAsync(
                    opacity: 1,
                    scale: 1,
                    milliseconds: 165);
            }
            finally
            {
                _windowTransitionInProgress =
                    false;
            }
        }

        private async void CloseWindow_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_windowTransitionInProgress)
            {
                return;
            }

            _windowTransitionInProgress =
                true;

            await AnimateWindowVisualAsync(
                opacity: 0,
                scale: 0.98,
                milliseconds: 105);

            SystemCommands.CloseWindow(
                this);
        }

        private async void MainWindow_StateChanged(
            object? sender,
            EventArgs e)
        {
            if (WindowState == WindowState.Minimized ||
                !_animateWindowAfterRestore)
            {
                return;
            }

            _animateWindowAfterRestore =
                false;

            _windowTransitionInProgress =
                true;

            try
            {
                await Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Render);

                await AnimateWindowVisualAsync(
                    opacity: 1,
                    scale: 1,
                    milliseconds: 175);
            }
            finally
            {
                _windowTransitionInProgress =
                    false;
            }
        }

        private Task AnimateWindowVisualAsync(
            double opacity,
            double scale,
            int milliseconds)
        {
            if (!SystemParameters.ClientAreaAnimation)
            {
                // Windows can ask applications to avoid decorative motion.
                // I still apply the final state so every window command works.
                WindowVisualRoot.Opacity =
                    opacity;

                WindowVisualScale.ScaleX =
                    scale;

                WindowVisualScale.ScaleY =
                    scale;

                return Task.CompletedTask;
            }

            TaskCompletionSource<bool> completion =
                new();

            Duration duration =
                TimeSpan.FromMilliseconds(
                    milliseconds);

            CubicEase easing =
                new()
                {
                    EasingMode = EasingMode.EaseOut
                };

            DoubleAnimation opacityAnimation =
                new()
                {
                    To = opacity,
                    Duration = duration,
                    EasingFunction = easing
                };

            DoubleAnimation scaleXAnimation =
                new()
                {
                    To = scale,
                    Duration = duration,
                    EasingFunction = easing
                };

            DoubleAnimation scaleYAnimation =
                new()
                {
                    To = scale,
                    Duration = duration,
                    EasingFunction = easing
                };

            opacityAnimation.Completed +=
                (_, _) =>
                {
                    // Committing the final values releases the animation
                    // clocks instead of leaving them attached to the window.
                    WindowVisualRoot.Opacity =
                        opacity;

                    WindowVisualScale.ScaleX =
                        scale;

                    WindowVisualScale.ScaleY =
                        scale;

                    WindowVisualRoot.BeginAnimation(
                        UIElement.OpacityProperty,
                        null);

                    WindowVisualScale.BeginAnimation(
                        ScaleTransform.ScaleXProperty,
                        null);

                    WindowVisualScale.BeginAnimation(
                        ScaleTransform.ScaleYProperty,
                        null);

                    completion.TrySetResult(
                        true);
                };

            WindowVisualRoot.BeginAnimation(
                UIElement.OpacityProperty,
                opacityAnimation,
                HandoffBehavior.SnapshotAndReplace);

            WindowVisualScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                scaleXAnimation,
                HandoffBehavior.SnapshotAndReplace);

            WindowVisualScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                scaleYAnimation,
                HandoffBehavior.SnapshotAndReplace);

            return completion.Task;
        }
        private void CloseLevelTransitionBlocker_Click(
    object sender,
    RoutedEventArgs e)
        {
            LevelTransitionBlocker.Visibility =
                Visibility.Collapsed;
        }

        private void NexusUsageChanged(
    NexusApiUsageSnapshot snapshot)
        {
            // I return to the UI thread because Nexus requests may finish
            // in the background while the Settings page is open.
            Dispatcher.BeginInvoke(
                new Action(
                    () => SettingsPageControl.ShowNexusUsage(
                        snapshot)));
        }

        private async void GameStatusTimer_Tick(
            object? sender,
            EventArgs e)
        {
            bool isGameRunning =
                !string.IsNullOrWhiteSpace(_gameDirectory) &&
                _gameProcessService.IsGameRunning(
                    _gameDirectory);

            bool gameJustStarted =
                isGameRunning &&
                !_wasGameRunning;

            bool gameJustStopped =
                !isGameRunning &&
                _wasGameRunning;

            // Update this before awaiting cleanup so another timer tick cannot
            // mistake the same shutdown for a second one.
            _wasGameRunning = isGameRunning;

            if (gameJustStopped)
            {
                _globalHotkeyService.Unregister();
                ClearLiveLoaderSessionBypass();

                _selectedLoaderMode =
                    LoaderLaunchMode.Normal;

                _hasInitialisedCurrentGameSession = false;
                _nextLiveMountOrder = 1000;

                string gameDirectory =
                    _gameDirectory!;

                // Give Unreal a moment to release the last file handles before
                // clearing Limelight's private staging folder.
                await Task.Delay(750);

                await Task.Run(() =>
                    _liveSessionService.RecoverClosedGame(
                        gameDirectory));
            }

            UpdateGameRunningStatus();
            await ApplyPendingDeploymentIfPossible();

            if (gameJustStarted)
            {
                if (_selectedLoaderMode !=
                    LoaderLaunchMode.Disabled)
                {
                    _liveSessionService.EnsureSession(
                        _gameDirectory!);

                    await InitialiseLiveLoaderForRunningGameAsync(
                        waitForGameProcess: false);
                }
            }

            RefreshSettingsPage();
            RefreshDiscordPresence(
                isGameRunning);
        }

        private void MainWindow_Closed(
            object? sender,
            EventArgs e)
        {
            // The timer belongs to this window, so there is no reason to leave
            // it checking processes after Limelight has closed.
            _resourceUsageOverlayWindow?.Close();
            _resourceUsageOverlayWindow = null;
            _gameStatusTimer.Stop();
            _globalHotkeyService.Dispose();
            _discordPresenceService.Dispose();

            // The bridge has already made its startup decision by this point.
            // Clearing the marker here keeps a later direct game launch normal.
            ClearLiveLoaderSessionBypass();
        }

        private void ClearLiveLoaderSessionBypass()
        {
            try
            {
                _liveLoaderBridgeService.SetSessionBypass(
                    isDisabled: false);
            }
            catch
            {
                // The marker expires by itself, so cleanup must never prevent
                // Limelight or the game from closing normally.
            }
        }

        private async Task InitialiseLiveLoaderForRunningGameAsync(
            bool waitForGameProcess)
        {
            if (_isLiveLoaderInitializationRunning ||
                _hasInitialisedCurrentGameSession ||
                _selectedLoaderMode ==
                    LoaderLaunchMode.Disabled ||
                string.IsNullOrWhiteSpace(_gameDirectory))
            {
                return;
            }

            string gameDirectory =
                _gameDirectory;

            Ue4ssDetectionResult loader =
                _ue4ssDetectionService.Detect(
                    gameDirectory);

            if (!loader.IsInstalled ||
    !_ue4ssConfigurationService.IsConfigured(loader) ||
    !_liveLoaderBridgeService.IsInstalled(loader) ||
    !_nativeBridgeInstallerService.IsCurrentVersionInstalled(
        loader))
            {
                // The optional loader has not been accepted yet. The normal
                // dashboard and setup prompt remain available.
                return;
            }

            _isLiveLoaderInitializationRunning = true;

            LiveLoaderInitializingWindow initialisingWindow =
               new LiveLoaderInitializingWindow();

            bool previousEnabledState =
                IsEnabled;

            Exception? initialisationFailure =
                null;

            try
            {
                IsEnabled = false;

                initialisingWindow.Report(
                    "WAITING FOR DEAD AS DISCO",
                    8,
                    "Limelight is waiting for the game process to start.");

                // I show the waiting card before Steam responds so a failed
                // handoff never looks like an unresponsive Launch button.
                initialisingWindow.Owner =
                    this;

                initialisingWindow.Show();

                DateTime processDeadline =
                    DateTime.UtcNow.AddSeconds(
                        waitForGameProcess
                            ? 75
                            : 10);

                while (!_gameProcessService.IsGameRunning(
                           gameDirectory))
                {
                    if (DateTime.UtcNow >= processDeadline)
                    {
                        throw new TimeoutException(
                            "Dead as Disco did not start before the live-loader check timed out.");
                    }

                    await Task.Delay(250);
                }

                _wasGameRunning = true;
                IntPtr gameWindowHandle =
                    IntPtr.Zero;

                DateTime gameWindowDeadline =
                    DateTime.UtcNow.AddSeconds(30);

                // I wait for Dead as Disco's visible window so the loading card
                // appears over the game instead of over Limelight.
                while (gameWindowHandle == IntPtr.Zero &&
                       DateTime.UtcNow < gameWindowDeadline)
                {
                    gameWindowHandle =
                        _gameProcessService.FindGameWindow(
                            gameDirectory);

                    if (gameWindowHandle == IntPtr.Zero)
                    {
                        await Task.Delay(100);
                    }
                }

                // The first card belongs to Limelight while Steam is opening
                // the game. I replace it here so the next card can belong to
                // Dead as Disco and stay above its loading screen.
                initialisingWindow.Close();

                initialisingWindow =
                    new LiveLoaderInitializingWindow();

                initialisingWindow.Report(
                    "CONNECTING TO UE4SS",
                    18,
                    "The game is running. Waiting for the Limelight runtime bridge and Unreal object system.");

                initialisingWindow.ShowOverGame(
                    gameWindowHandle);

                DateTime bridgeDeadline =
                    DateTime.UtcNow.AddMinutes(2);

                DateTime? gameMissingSince = null;

                while (!_liveLoaderBridgeService.IsOnline())
                {
                    bool gameIsRunning =
                        _gameProcessService.IsGameRunning(
                            gameDirectory);

                    if (gameIsRunning)
                    {
                        gameMissingSince = null;
                    }
                    else
                    {
                        gameMissingSince ??= DateTime.UtcNow;
                    }

                    // Windows can briefly omit a process while the launcher
                    // hands control to the shipping executable. I wait for a
                    // sustained absence before treating the game as closed.
                    if (gameMissingSince.HasValue &&
                        DateTime.UtcNow - gameMissingSince.Value >=
                        TimeSpan.FromSeconds(8))
                    {
                        throw new InvalidOperationException(
                            "Dead as Disco closed before the live loader was ready.");
                    }

                    if (DateTime.UtcNow >= bridgeDeadline)
                    {
                        throw new TimeoutException(
                            "UE4SS did not bring the Limelight bridge online in time.");
                    }

                    await Task.Delay(300);
                }

                initialisingWindow.Report(
                    "VERIFYING NATIVE BRIDGE",
                    27,
                    "Limelight is checking the transition-safe package mounting bridge.");

                LiveLoaderCommandResult nativePing =
                    await _liveLoaderCommandService.PingNativeAsync();

                if (!nativePing.Success)
                {
                    throw new InvalidOperationException(
                        nativePing.Message);
                }

                initialisingWindow.Report(
                    "PREPARING THE MOUNT BRIDGE",
                    29,
                    "Limelight is locating Unreal's live-mount functions. The first scan can take a little while, but the game will remain responsive.");

                LiveLoaderCommandResult mountResolver =
                    await _liveLoaderCommandService
                        .ScanMountFunctionsAsync();

                if (!mountResolver.Success)
                {
                    throw new InvalidOperationException(
                        mountResolver.Message);
                }

                InstalledMod? activeMod =
                    _settings.InstalledMods.FirstOrDefault(mod =>
                        string.Equals(
                            mod.Id,
                            _settings.ActiveModId,
                            StringComparison.OrdinalIgnoreCase) &&
                        Directory.Exists(
                            mod.InstallDirectory));

                LiveLoaderCommandResult startupSafety =
                    await WaitForInitialLiveWorldAsync(
                        gameDirectory,
                        (phase, progress) =>
                            initialisingWindow.Report(
                                phase,
                                progress));

                if (!startupSafety.Success)
                {
                    throw new InvalidOperationException(
                        startupSafety.Message);
                }

                if (activeMod is not null)
                {
                    await ActivateLiveModAsync(
                        activeMod,
                        gameDirectory,
                        (phase, progress) =>
                            initialisingWindow.Report(
                                phase,
                                progress),
                        allowDeferredCharlieRefresh: true);
                }
                else
                {
                    initialisingWindow.Report(
                        "LIVE LOADER READY",
                        100,
                        "The runtime is online. No active model mod needs to be mounted.");
                }

                _hasInitialisedCurrentGameSession = true;

                await Task.Delay(650);
            }
            catch (Exception exception)
            {
                initialisationFailure = exception;
            }
            finally
            {
                if (initialisingWindow.IsVisible)
                {
                    initialisingWindow.Close();
                }

                IsEnabled = previousEnabledState;
                _isLiveLoaderInitializationRunning = false;
                UpdateGameRunningStatus();
            }

            if (initialisationFailure is not null)
            {
                ShowLimelightDialog(
                    "LIVE LOADER COULD NOT INITIALISE",
                    "Dead as Disco can still be played normally, but live switching will remain locked for this launch.",
                    LimelightDialogTone.Warning,
                    details: initialisationFailure.Message,
                    eyebrow: "LIVE LOADER");
            }
        }

        private void UpdateGameRunningStatus()
        {
            if (_isLiveLoaderSetupRunning)
            {
                return;
            }

            if (_isLiveModChangeRunning)
            {
                return;
            }

            if (_isLiveLoaderInitializationRunning)
            {
                return;
            }

            string? gameDirectory =
                _gameDirectory;

            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                GameProcessStatusText.Text =
                    "NOT CONNECTED";

                GameProcessStatusText.Foreground =
                    (Brush)FindResource("MutedTextBrush");

                SetLiveLoaderDisplay(
                    "NOT CONNECTED",
                    "Connect Dead as Disco before setting up live character switching.",
                    isHealthy: false);

                return;
            }

            bool isGameRunning =
                _gameProcessService.IsGameRunning(
                    gameDirectory);

            if (isGameRunning)
            {
                _pendingDeploymentAttempted = false;

                GameProcessStatusText.Text =
                    "RUNNING";

                GameProcessStatusText.Foreground =
                    (Brush)FindResource("CyanBrush");
            }
            else
            {
                GameProcessStatusText.Text =
                    "NOT RUNNING";

                GameProcessStatusText.Foreground =
                    (Brush)FindResource("PinkBrush");
            }

            if (isGameRunning &&
                _selectedLoaderMode ==
                    LoaderLaunchMode.Disabled)
            {
                SetLiveLoaderDisplay(
                    "DISABLED",
                    "This session is using the deployed mod without live switching, loader scans, or X19 controls.",
                    isHealthy: true);

                return;
            }

            Ue4ssDetectionResult loader =
                _ue4ssDetectionService.Detect(
                    gameDirectory);

            if (loader.IsPartiallyInstalled)
            {
                SetLiveLoaderDisplay(
                    "REPAIR NEEDED",
                    "The loader installation is incomplete. Close the game and use Repair Live Loader in Settings.",
                    isHealthy: false);

                return;
            }

            if (!loader.IsInstalled)
            {
                SetLiveLoaderDisplay(
                    "NOT INSTALLED",
                    "Set up the Live Loader to switch character mods without restarting the game.",
                    isHealthy: false);

                return;
            }

            if (!_ue4ssConfigurationService.IsConfigured(loader))
            {
                SetLiveLoaderDisplay(
                    "SETUP NEEDED",
                    "Limelight needs to finish configuring the loader for Dead as Disco.",
                    isHealthy: false);

                return;
            }

            if (!_liveLoaderBridgeService.IsInstalled(loader))
            {
                SetLiveLoaderDisplay(
                    "BRIDGE NEEDED",
                    "Limelight's communication bridge is missing and can be restored from Settings.",
                    isHealthy: false);

                return;
            }

            if (!_nativeBridgeInstallerService.IsCurrentVersionInstalled(
        loader))
            {
                SetLiveLoaderDisplay(
                    "NATIVE BRIDGE NEEDED",
                    "Limelight's native companion is missing or does not match this version. Use Repair Live Loader in Settings.",
                    isHealthy: false);

                return;
            }

            if (!isGameRunning)
            {
                SetLiveLoaderDisplay(
                    "READY",
                    "The Live Loader is installed and will come online when Dead as Disco starts.",
                    isHealthy: true);

                return;
            }

            if (_liveLoaderBridgeService.IsOnline())
            {
                SetLiveLoaderDisplay(
                    "ONLINE",
                    "Live character switching is available for the current game session.",
                    isHealthy: true);

                return;
            }

            // UE4SS is installed and the game exists, but the Lua bridge has not
            // produced a recent heartbeat.
            SetLiveLoaderDisplay(
                "OFFLINE",
                "Dead as Disco is running, but Limelight has not received a loader heartbeat yet.",
                isHealthy: false);
        }

        private void SetLiveLoaderDisplay(
            string status,
            string description,
            bool isHealthy)
        {
            Brush statusBrush =
                (Brush)FindResource(
                    isHealthy
                        ? "CyanBrush"
                        : "PinkBrush");

            LiveLoaderStatusText.Text =
                status;

            LiveLoaderStatusText.Foreground =
                statusBrush;

            LiveLoaderStatusDescriptionText.Text =
                description;

            LiveLoaderStatusDot.Fill =
                statusBrush;

            LiveLoaderStatusRing.Stroke =
                statusBrush;
        }

        private async Task ApplyPendingDeploymentIfPossible()
        {
            if (_isApplyingPendingDeployment ||
                _isLiveModChangeRunning ||
                _pendingDeploymentAttempted ||
                string.IsNullOrWhiteSpace(
                    _settings.PendingDeploymentModId) ||
                string.IsNullOrWhiteSpace(
                    _gameDirectory) ||
                _gameProcessService.IsGameRunning(
                    _gameDirectory))
            {
                return;
            }

            InstalledMod? pendingMod =
                _settings.InstalledMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        _settings.PendingDeploymentModId,
                        StringComparison.OrdinalIgnoreCase));

            if (pendingMod == null ||
                !Directory.Exists(
                    pendingMod.InstallDirectory))
            {
                _settings.PendingDeploymentModId =
                    string.Empty;

                _settingsService.Save(_settings);
                return;
            }

            _isApplyingPendingDeployment = true;
            _pendingDeploymentAttempted = true;

            try
            {
                string gameDirectory =
                    _gameDirectory;

                await Task.Run(() =>
                    _modDeploymentService.Activate(
                        pendingMod,
                        gameDirectory));

                _settings.PendingDeploymentModId =
                    string.Empty;

                _settingsService.Save(_settings);
            }
            catch
            {
                // Keep the pending ID. Limelight can try again the next time
                // it opens while the game is fully closed.
            }
            finally
            {
                _isApplyingPendingDeployment = false;
            }
        }

        private async Task ShowLiveLoaderSetupPromptIfNeeded()
        {
            if (_hasHandledLiveLoaderPrompt ||
                _isLiveLoaderSetupRunning)
            {
                return;
            }

            string? gameDirectory =
                _gameDirectory;

            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                return;
            }

            LocalCompatibilityResult compatibility =
                _compatibilityService.Check(
                    gameDirectory);

            WriteLaunchTrace(
                "Compatibility checked: " +
                $"liveLoader={compatibility.IsLiveLoaderCompatible}; " +
                $"gameConnected={compatibility.GameConnected}; " +
                $"buildDetected={compatibility.GameBuildDetected}; " +
                $"buildCompatible={compatibility.GameBuildCompatible}; " +
                $"embeddedPayload={compatibility.EmbeddedPayloadCompatible}; " +
                $"ue4ssInstalled={compatibility.Ue4ssInstalled}; " +
                $"ue4ssCompatible={compatibility.Ue4ssCompatible}; " +
                $"ue4ssConfigured={compatibility.Ue4ssConfigured}; " +
                $"luaBridge={compatibility.LuaBridgeInstalled}; " +
                $"nativeBridge={compatibility.NativeBridgeCurrent}; " +
                $"detail={compatibility.Detail}");

            if (!compatibility.GameBuildDetected ||
                !compatibility.GameBuildCompatible)
            {
                // I leave ordinary mod management alone on an unknown game
                // build. Only the version-sensitive Live Loader is held back.
                _hasHandledLiveLoaderPrompt = true;
                return;
            }

            Ue4ssDetectionResult currentInstallation =
                _ue4ssDetectionService.Detect(
                    gameDirectory);

            bool isGameRunning =
                _gameProcessService.IsGameRunning(
                    gameDirectory);

            if (currentInstallation.IsInstalled &&
                _ue4ssConfigurationService.IsRuntimeCompatible(
                    currentInstallation) &&
                _liveLoaderBridgeService.HasBridgeFiles(
                    currentInstallation) &&
                !isGameRunning)
            {
                try
                {
                    // Once the user has accepted setup, repair both our known
                    // game configuration and bridge registration when needed.
                    _ue4ssConfigurationService.Apply(
                        currentInstallation);

                    _liveLoaderBridgeService.EnsureInstalled(
                        currentInstallation);

                    _nativeBridgeInstallerService.EnsureInstalled(
    currentInstallation);
                }
                catch
                {
                    // The normal setup popup below can explain and retry a
                    // repair if Windows has temporarily locked the file.
                }
            }

            if (currentInstallation.IsInstalled &&
     _ue4ssConfigurationService.IsConfigured(
         currentInstallation) &&
     _liveLoaderBridgeService.IsInstalled(
         currentInstallation) &&
     _nativeBridgeInstallerService.IsCurrentVersionInstalled(
         currentInstallation))
            {
                if (!isGameRunning)
                {
                    // Limelight owns this script, so it can safely update the bridge
                    // without modifying the user's other UE4SS mods.
                    _liveLoaderBridgeService.EnsureInstalled(
                        currentInstallation);
                }

                _hasHandledLiveLoaderPrompt = true;
                return;
            }

            _hasHandledLiveLoaderPrompt = true;

            LiveLoaderSetupWindow setupWindow =
                new LiveLoaderSetupWindow
                {
                    Owner = this
                };

            setupWindow.ShowDialog();

            if (setupWindow.PromptDismissed)
            {
                // Store the actual directory rather than one global yes/no value. A
                // different installation should receive its own setup choice.
                _settings.DismissedLiveLoaderPromptForGameDirectory =
                    gameDirectory;

                _settingsService.Save(_settings);
                return;
            }

            if (!setupWindow.SetupRequested)
            {
                return;
            }

            if (_gameProcessService.IsGameRunning(gameDirectory))
            {
                ShowLimelightDialog(
                    "CLOSE THE GAME FIRST",
                    "Dead as Disco must be closed before Limelight can set up the Live Loader. Limelight will ask again next time it starts.",
                    LimelightDialogTone.Warning,
                    eyebrow: "SETUP PAUSED");

                return;
            }

            _isLiveLoaderSetupRunning = true;

            bool previousEnabledState =
                IsEnabled;

            Ue4ssPackageDownload? downloadedPackage =
                null;

            Ue4ssInstallResult? installResult =
                null;

            Exception? setupFailure =
                null;

            try
            {
                IsEnabled = false;
                Mouse.OverrideCursor = Cursors.Wait;
                Ue4ssDetectionResult installedLoader =
    _ue4ssDetectionService.Detect(
        gameDirectory);

                if (!installedLoader.IsInstalled ||
                    !_ue4ssConfigurationService.IsRuntimeCompatible(
                        installedLoader))
                {
                    LiveLoaderStatusText.Text =
                        "DOWNLOADING";

                    LiveLoaderStatusText.Foreground =
                        (Brush)FindResource("CyanBrush");

                    downloadedPackage =
                        await _ue4ssReleaseService.DownloadAsync();

                    // The user could start the game through Steam while the download is
                    // running, so check again before changing anything in Win64.
                    if (_gameProcessService.IsGameRunning(gameDirectory))
                    {
                        throw new InvalidOperationException(
                            "Dead as Disco started while the loader was downloading. " +
                            "Close the game and try the setup again.");
                    }

                    LiveLoaderStatusText.Text =
                        "INSTALLING";

                    installResult =
                        await _ue4ssInstallerService.InstallAsync(
                            gameDirectory,
                            downloadedPackage.PackagePath);

                    installedLoader =
                        _ue4ssDetectionService.Detect(
                            gameDirectory);

                    if (!installedLoader.IsInstalled ||
                        !_ue4ssConfigurationService.IsRuntimeCompatible(
                            installedLoader))
                    {
                        throw new InvalidOperationException(
                            "The compatible live-loader files could not be verified after installation.");
                    }
                }

                LiveLoaderStatusText.Text =
                    "CONFIGURING";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("CyanBrush");

                // Apply the Dead as Disco signatures and quiet public-facing
                // settings before the bridge is registered.
                _ue4ssConfigurationService.Apply(
                    installedLoader);

                if (!_ue4ssConfigurationService.IsConfigured(
                        installedLoader))
                {
                    throw new InvalidOperationException(
                        "The Dead as Disco live-loader configuration could not be verified.");
                }

                LiveLoaderStatusText.Text =
                    "ADDING BRIDGE";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("CyanBrush");

                // The bridge is Limelight's own Lua mod. Existing UE4SS settings and other
                // installed Lua mods are left in place.
                _liveLoaderBridgeService.EnsureInstalled(
                    installedLoader);

                if (!_liveLoaderBridgeService.IsInstalled(
                        installedLoader))
                {
                    throw new InvalidOperationException(
                        "The Limelight runtime bridge could not be verified.");
                }

                LiveLoaderStatusText.Text =
 "ADDING NATIVE BRIDGE";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("CyanBrush");

                // I install the native companion only after UE4SS and the Lua bridge
                // have both passed their checks.
                _nativeBridgeInstallerService.EnsureInstalled(
                    installedLoader);

                if (!_nativeBridgeInstallerService.IsCurrentVersionInstalled(
                        installedLoader))
                {
                    throw new InvalidOperationException(
                        "The Limelight native bridge could not be verified.");
                }

                _settings.DismissedLiveLoaderPromptForGameDirectory =
                    string.Empty;

                _settingsService.Save(_settings);
            }
            catch (Exception exception)
            {
                setupFailure = exception;
            }
            finally
            {
                if (downloadedPackage is not null)
                {
                    try
                    {
                        // The installed files and any rollback backup are elsewhere,
                        // so the downloaded ZIP is no longer needed.
                        File.Delete(
                            downloadedPackage.PackagePath);
                    }
                    catch
                    {
                        // Windows can clear this temporary file later.
                    }
                }

                IsEnabled = previousEnabledState;
                Mouse.OverrideCursor = null;

                _isLiveLoaderSetupRunning = false;
                UpdateGameRunningStatus();
            }

            if (setupFailure is not null)
            {
                ShowLimelightDialog(
                    "LIVE LOADER SETUP FAILED",
                    "No mod-library features were disabled, so Limelight can still manage imported mods normally.",
                    LimelightDialogTone.Error,
                    details: setupFailure.Message,
                    eyebrow: "SETUP MISSED ITS CUE");

                return;
            }

            string backupMessage =
                installResult?.CreatedBackup == true
                    ? "\n\nExisting loader files were backed up before installation."
                    : string.Empty;

            ShowLimelightDialog(
                "LIVE LOADER READY",
                "The Live Loader was set up successfully. It will start the next time Dead as Disco launches." +
                backupMessage,
                LimelightDialogTone.Success,
                eyebrow: "SETUP COMPLETE");
        }

        private async Task CheckForExistingMods()
        {
            if (string.IsNullOrWhiteSpace(_gameDirectory))
            {
                return;
            }

            string gameDirectory =
                _gameDirectory;

            int existingModCount =
                _existingModsMigrationService.CountExistingMods(
                    gameDirectory);

            if (existingModCount == 0)
            {
                return;
            }

            string modLabel =
                existingModCount == 1
                    ? "1 existing mod"
                    : $"{existingModCount} existing mods";

            LimelightDialogChoice choice =
                ShowLimelightDialog(
                    "EXISTING MODS FOUND",
                    $"Limelight found {modLabel} inside the game's ~mods folder. Would you like to move them into the Limelight library?",
                    LimelightDialogTone.Question,
                    primaryAction: "MOVE MODS",
                    secondaryAction: "NOT NOW",
                    footerHint: "FILES STAY IN PLACE UNTIL THE LIBRARY IS SAVED");

            if (choice != LimelightDialogChoice.Primary)
            {
                return;
            }

            try
            {
                List<InstalledMod> librarySnapshot =
                    _settings.InstalledMods.ToList();

                ExistingModsMigrationPlan plan =
                    await Task.Run(() =>
                        _existingModsMigrationService.PrepareMigration(
                            gameDirectory,
                            librarySnapshot));

                _settings.InstalledMods.AddRange(
                    plan.ImportedMods);

                _settingsService.Save(_settings);

                // Originals are removed only after settings.json contains
                // every successfully prepared library entry.
                await Task.Run(() =>
                    _existingModsMigrationService.CompleteMigration(
                        plan));

                RefreshLibrarySummary();

                ShowLimelightDialog(
                    "MODS JOINED THE LIBRARY",
                    "The existing mods were moved into Limelight successfully. Choose the model you want and select Activate.",
                    LimelightDialogTone.Success,
                    eyebrow: "MIGRATION COMPLETE");
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "MIGRATION COULD NOT FINISH",
                    "Limelight left the existing files in place.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "MIGRATION FAILED");
            }
        }

        private async Task<List<ModAssetPackage>>
            GetLivePackagesAsync(
                InstalledMod mod)
        {
            if (mod.AssetPackages.Count == 0 ||
                mod.AssetManifestVersion <
                    ModAssetScannerService.CurrentManifestVersion)
            {
                mod.AssetPackages =
                    await Task.Run(() =>
                        _modLibraryService.ScanAssets(
                            mod));

                mod.AssetManifestVersion =
                    ModAssetScannerService.CurrentManifestVersion;

                _settingsService.Save(_settings);
            }

            return mod.AssetPackages
                .Where(package =>
                    package.IsSafeForLiveReload)
                .OrderBy(package =>
                    package.ReloadPriority)
                .ThenBy(package =>
                    package.PackagePath)
                .ToList();
        }

        private async Task RetireStaleLiveContainersAsync(
            string gameDirectory,
            Action<string, int>? reportProgress)
        {
            List<LiveSessionMountRecord> staleContainers =
                _liveSessionService.GetRetirableMountedContainers(
                    gameDirectory);

            if (staleContainers.Count == 0)
            {
                return;
            }

            reportProgress?.Invoke(
                "RETIRING PREVIOUS CONTAINER",
                20);

            foreach (LiveSessionMountRecord staleContainer in
                     staleContainers)
            {
                // I check both sides of the unmount because a level change
                // can begin while the native bridge is finishing its work.
                await EnsureLiveWorldStableAsync();

                LiveLoaderCommandResult unmountResult =
                    await _liveLoaderCommandService.UnmountPakAsync(
                        staleContainer.PakPath);

                if (!unmountResult.Success)
                {
                    _liveSessionService.RecordRetirementFailure(
                        staleContainer.PakPath,
                        unmountResult.Message);

                    throw new InvalidOperationException(
                        "Limelight could not retire the previous live container. " +
                        unmountResult.Message +
                        " Wait until the current level is fully visible, then try again.");
                }

                await EnsureLiveWorldStableAsync();

                _liveSessionService.RecordUnmountedContainer(
                    staleContainer.PakPath);

                LiveSessionCleanupResult cleanup =
                    _liveSessionService.DeleteRetiredContainerFiles(
                        staleContainer.PakPath,
                        gameDirectory);

                if (cleanup.Errors.Count > 0)
                {
                    // The slot is safe to reuse once Unreal confirms the
                    // unmount. Busy files can wait for closed-game cleanup.
                    _liveSessionService.RecordRetirementFailure(
                        staleContainer.PakPath,
                        string.Join(
                            "; ",
                            cleanup.Errors));
                }
            }
        }

        private async Task ActivateLiveModAsync(
            InstalledMod mod,
            string gameDirectory,
            Action<string, int>? reportProgress = null,
            bool allowDeferredCharlieRefresh = false)
        {
            int upcomingContainerCount =
                _liveModStagingService.CountContainers(
                    mod);

            if (upcomingContainerCount == 0)
            {
                throw new InvalidDataException(
                    $"{mod.DisplayName} does not contain a complete pak, utoc, and ucas set.");
            }

            await EnsureLiveWorldStableAsync();

            // I keep the active generation and the incoming generation only.
            // This stops X19 rotations from consuming a new safety slot on
            // every press while leaving the current assets available until
            // their replacement is ready to mount.
            await RetireStaleLiveContainersAsync(
                gameDirectory,
                reportProgress);

            if (!_liveSessionService.CanStageContainers(
                    gameDirectory,
                    upcomingContainerCount,
                    out string limitMessage))
            {
                throw new InvalidOperationException(
                    limitMessage);
            }

            string generationId =
                _liveSessionService.BeginActivation(
                mod,
                gameDirectory);

            try
            {
                reportProgress?.Invoke(
                    "SCANNING MOD CONTENT",
                    35);

                List<ModAssetPackage> livePackages =
                    await GetLivePackagesAsync(mod);

                if (livePackages.Count == 0)
                {
                    throw new InvalidDataException(
                        "This mod does not contain any assets Limelight can safely refresh live.");
                }

                if (!livePackages.Any(package =>
                        package.IsCharlieMesh))
                {
                    throw new InvalidDataException(
                        "This mod does not replace SK_Charlie, so Limelight will not live-mount it automatically.");
                }

                reportProgress?.Invoke(
                    "STAGING MOD CONTAINER",
                    48);

                LiveModStageResult stageResult =
                    await Task.Run(() =>
                        _liveModStagingService.Stage(
                            mod,
                            gameDirectory));

                await EnsureLiveWorldStableAsync();

                _liveSessionService.RecordStagedContainers(
                    mod,
                    stageResult.PakPaths,
                    gameDirectory,
                    generationId);

                reportProgress?.Invoke(
                    "MOUNTING MOD CONTENT",
                    60);

                foreach (string pakPath in
                         stageResult.PakPaths)
                {
                    await EnsureLiveWorldStableAsync();

                    int mountOrder =
                        _nextLiveMountOrder++;

                    _liveSessionService.RecordMountAttempt(
                        pakPath,
                        mountOrder);

                    LiveLoaderCommandResult mountResult =
                        await _liveLoaderCommandService.MountPakAsync(
                            pakPath,
                            mountOrder);

                    if (!mountResult.Success)
                    {
                        if (!mountResult.Message.Contains(
                                "did not respond",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            // A definite rejection means Unreal never owned this
                            // container, so failed-stage cleanup may remove it.
                            _liveSessionService.RecordRejectedMount(
                                pakPath);
                        }

                        throw new InvalidOperationException(
                            mountResult.Message);
                    }

                    _liveSessionService.RecordMountedContainer(
                        pakPath,
                        mountOrder);
                }

                reportProgress?.Invoke(
                    "REFRESHING OVERRIDDEN PACKAGES",
                    74);

                await EnsureLiveWorldStableAsync();

                LiveLoaderCommandResult releaseResult =
                    await _liveLoaderCommandService.ReleasePackagesAsync(
                        livePackages.Select(package =>
                            package.PackagePath));

                if (!releaseResult.Success)
                {
                    throw new InvalidOperationException(
                        releaseResult.Message);
                }

                reportProgress?.Invoke(
                    "LOADING MODELS, PORTRAITS AND TEXT",
                    86);

                await EnsureLiveWorldStableAsync();

                List<ModAssetPackage> dependencyPackages =
                    livePackages
                        .Where(package =>
                            package.Kind !=
                                ModAssetKind.SkeletalMesh)
                        .ToList();

                List<ModAssetPackage> meshPackages =
                    livePackages
                        .Where(package =>
                            package.Kind ==
                                ModAssetKind.SkeletalMesh)
                        .ToList();

                LiveLoaderCommandResult dependencyReloadResult =
                    dependencyPackages.Count == 0
                        ? new LiveLoaderCommandResult
                        {
                            Success = true
                        }
                        : await _liveLoaderCommandService.ReloadAssetsAsync(
                            dependencyPackages.Select(package =>
                                package.ObjectPath));

                if (!dependencyReloadResult.Success)
                {
                    throw new InvalidOperationException(
                        dependencyReloadResult.Message);
                }

                LiveLoaderCommandResult meshReloadResult =
                    await _liveLoaderCommandService.ReloadAssetsAsync(
                        meshPackages.Select(package =>
                            package.ObjectPath));

                if (!meshReloadResult.Success)
                {
                    throw new InvalidOperationException(
                        meshReloadResult.Message);
                }

                if (dependencyPackages.Count > 0)
                {
                    // Some material dependencies do not become loadable until
                    // the replacement mesh has opened its package. A short
                    // second pass fills those references before Charlie is
                    // reapplied, which prevents an otherwise valid model from
                    // appearing black.
                    await Task.Delay(180);
                    await EnsureLiveWorldStableAsync();

                    LiveLoaderCommandResult dependencyRetryResult =
                        await _liveLoaderCommandService.ReloadAssetsAsync(
                            dependencyPackages.Select(package =>
                                package.ObjectPath));

                    if (!dependencyRetryResult.Success)
                    {
                        throw new InvalidOperationException(
                            dependencyRetryResult.Message);
                    }
                }

                List<ModAssetPackage> renderedDependencies =
                    dependencyPackages
                        .Where(package =>
                            package.Kind == ModAssetKind.Texture ||
                            package.Kind == ModAssetKind.Material)
                        .ToList();

                int[] retryDelaysMilliseconds =
                {
                    150,
                    250,
                    400,
                    650,
                    900,
                    1200
                };

                LiveLoaderCommandResult reapplyResult =
                    new LiveLoaderCommandResult
                    {
                        Success = false,
                        Message = "The replacement model was not verified."
                    };

                LiveLoaderCommandResult dependencyVerificationResult =
                    new LiveLoaderCommandResult
                    {
                        Success = true
                    };

                bool deferredCharlieRefresh = false;

                for (int attempt = 0;
                     attempt < retryDelaysMilliseconds.Length;
                     attempt++)
                {
                    await EnsureLiveWorldStableAsync();

                    dependencyVerificationResult =
                        renderedDependencies.Count == 0
                            ? new LiveLoaderCommandResult
                            {
                                Success = true
                            }
                            : await _liveLoaderCommandService.VerifyAssetsAsync(
                                renderedDependencies.Select(package =>
                                    package.ObjectPath));

                    if (dependencyVerificationResult.Success)
                    {
                        reapplyResult =
                            await _liveLoaderCommandService.ReapplyCharlieAsync();

                        if (reapplyResult.Success)
                        {
                            break;
                        }

                        bool playerHasNotAppeared =
                            reapplyResult.Message.Contains(
                                "No active Charlie pawn",
                                StringComparison.OrdinalIgnoreCase);

                        if (allowDeferredCharlieRefresh &&
                            playerHasNotAppeared)
                        {
                            deferredCharlieRefresh = true;
                            break;
                        }
                    }

                    if (attempt ==
                        retryDelaysMilliseconds.Length - 1)
                    {
                        string failureMessage =
                            dependencyVerificationResult.Success
                                ? reapplyResult.Message
                                : dependencyVerificationResult.Message;

                        throw new InvalidOperationException(
                            failureMessage);
                    }

                    // Cooked dependencies can finish registering just after
                    // SK_Charlie opens. I retry with a small backoff instead of
                    // accepting Unreal's black fallback material as success.
                    await Task.Delay(
                        retryDelaysMilliseconds[attempt]);
                }

                if (reapplyResult.Success)
                {
                    LiveLoaderCommandResult retirementResult =
                        await _liveLoaderCommandService
                            .ConfirmPackageRetirementAsync();

                    if (!retirementResult.Success)
                    {
                        throw new InvalidOperationException(
                            retirementResult.Message);
                    }
                }

                if (dependencyPackages.Count > 0)
                {
                    // The automatic world refresh needs every non-mesh asset,
                    // not only the strict material verification subset.
                    LiveLoaderCommandResult rememberedAssetsResult =
                        await _liveLoaderCommandService.ReloadAssetsAsync(
                            dependencyPackages.Select(package =>
                                package.ObjectPath));

                    if (!rememberedAssetsResult.Success)
                    {
                        throw new InvalidOperationException(
                            rememberedAssetsResult.Message);
                    }
                }

                reportProgress?.Invoke(
                    deferredCharlieRefresh
                        ? "READY: CHARLIE WILL REFRESH WHEN SHE APPEARS"
                        : "LIVE LOADER READY",
                    100);

                _liveSessionService.CompleteActivation(
                    mod,
                    generationId);
            }
            catch (Exception exception)
            {
                // Anything Unreal already mounted stays recorded for the guarded
                // retirement path. Files which never mounted are safe to remove now.
                _liveSessionService.DeleteUncommittedGenerationFiles(
                    generationId,
                    gameDirectory);

                _liveSessionService.FailActivation(
                    exception);

                throw;
            }
        }

        private async Task EnsureLiveWorldStableAsync()
        {
            LiveLoaderCommandResult result =
                await _liveLoaderCommandService
                    .IsWorldStableAsync();

            if (!result.Success)
            {
                throw new InvalidOperationException(
                    result.Message);
            }
        }

        private List<InstalledMod> GetX19Rotation()
        {
            // I rebuild the rotation from the current library so removed mods
            // can never leave a dead entry behind in the hotkey cycle.
            return _settings.X19LoaderModIds
                .Select(id =>
                    _settings.InstalledMods.FirstOrDefault(mod =>
                        string.Equals(
                            mod.Id,
                            id,
                            StringComparison.OrdinalIgnoreCase)))
                .Where(mod =>
                    mod is not null &&
                    Directory.Exists(mod.InstallDirectory))
                .Cast<InstalledMod>()
                .ToList();
        }

        private int GetNextX19RotationIndex(
            int rotationCount,
            int currentIndex)
        {
            if (rotationCount <= 1)
            {
                return 0;
            }

            if (!_settings.X19ShuffleEnabled)
            {
                return currentIndex < 0
                    ? 0
                    : (currentIndex + 1) % rotationCount;
            }

            if (currentIndex < 0)
            {
                return Random.Shared.Next(rotationCount);
            }

            // The offset starts at one, so shuffle still feels random without
            // choosing the character which is already on stage.
            int offset =
                Random.Shared.Next(
                    1,
                    rotationCount);

            return (currentIndex + offset) % rotationCount;
        }

        private void EnableX19Hotkey()
        {
            _globalHotkeyService.Unregister();

            if (_selectedLoaderMode !=
                LoaderLaunchMode.X19)
            {
                return;
            }

            if (_globalHotkeyService.Register(
                    this,
                    _settings.X19HotkeyGesture,
                    () =>
                        _gameProcessService.IsGameWindowForeground(
                            _gameDirectory),
                    out string errorMessage))
            {
                return;
            }

            _selectedLoaderMode =
                LoaderLaunchMode.Normal;

            ShowNotification(
                "X19 HOTKEY UNAVAILABLE",
                errorMessage +
                " Limelight will use the normal Live Loader for this session.",
                isError: true);
        }

        private async void X19HotkeyPressed()
        {
            if (_selectedLoaderMode != LoaderLaunchMode.X19 ||
                _isLiveModChangeRunning ||
                _isX19SafetyProbeRunning ||
                string.IsNullOrWhiteSpace(_gameDirectory) ||
                !_gameProcessService.IsGameRunning(
                    _gameDirectory) ||
                !_gameProcessService.IsGameWindowForeground(
                    _gameDirectory))
            {
                return;
            }

            _isX19SafetyProbeRunning = true;

            try
            {
                // X19 never queues a key press. If Unreal is loading, streaming,
                // or retiring the previous model, the current character stays put.
                LiveLoaderCommandResult safetyCheck =
                    await _liveLoaderCommandService
                        .CanSwitchModsAsync();

                if (!safetyCheck.Success ||
                    _selectedLoaderMode != LoaderLaunchMode.X19 ||
                    _isLiveModChangeRunning ||
                    !_gameProcessService.IsGameWindowForeground(
                        _gameDirectory))
                {
                    ShowX19BlockedPulse();
                    return;
                }

                List<InstalledMod> rotation =
                    GetX19Rotation();

                if (rotation.Count == 0)
                {
                    ShowX19BlockedPulse();
                    return;
                }

                int currentIndex =
                    rotation.FindIndex(mod =>
                        string.Equals(
                            mod.Id,
                            _settings.ActiveModId,
                            StringComparison.OrdinalIgnoreCase));

                int nextIndex =
                    GetNextX19RotationIndex(
                        rotation.Count,
                        currentIndex);

                InstalledMod nextMod =
                    rotation[nextIndex];

                if (rotation.Count == 1 &&
                    string.Equals(
                        nextMod.Id,
                        _settings.ActiveModId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    X19SwitchPulseWindow completePulse =
                        new X19SwitchPulseWindow();

                    completePulse.ShowOverGame(
                        _gameProcessService.FindGameWindow(
                            _gameDirectory));

                    completePulse.ShowSuccess();
                    return;
                }

                _isX19SwitchRequest = true;
                ToggleModRequested(
                    nextMod.Id);
            }
            catch
            {
                ShowX19BlockedPulse();
            }
            finally
            {
                _isX19SafetyProbeRunning = false;
            }
        }

        private void ShowX19BlockedPulse()
        {
            X19SwitchPulseWindow errorPulse =
                new X19SwitchPulseWindow();

            errorPulse.ShowOverGame(
                _gameProcessService.FindGameWindow(
                    _gameDirectory));

            errorPulse.ShowError();
        }

        private async void ToggleModRequested(
    string modId)
        {
            if (_isLiveModChangeRunning)
            {
                _isX19SwitchRequest = false;
                return;
            }

            InstalledMod? selectedMod =
                _settings.InstalledMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        modId,
                        StringComparison.OrdinalIgnoreCase));

            if (selectedMod == null)
            {
                _isX19SwitchRequest = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(_gameDirectory))
            {
                ShowNotification(
                    "GAME NOT CONNECTED",
                    "Connect the Dead as Disco installation before activating a mod.",
                    isError: true);

                _isX19SwitchRequest = false;
                return;
            }

            string gameDirectory =
                _gameDirectory;

            bool isCurrentlyActive =
                string.Equals(
                    _settings.ActiveModId,
                    selectedMod.Id,
                    StringComparison.OrdinalIgnoreCase);

            bool isGameRunning =
                _gameProcessService.IsGameRunning(
                    gameDirectory);

            if (isGameRunning &&
                _selectedLoaderMode ==
                    LoaderLaunchMode.Disabled)
            {
                ShowNotification(
                    "LIVE LOADER DISABLED",
                    "This game session was launched without live switching. Close Dead as Disco before changing the deployed mod.",
                    isError: true);

                _isX19SwitchRequest = false;
                return;
            }

            bool useX19Pulse =
                _isX19SwitchRequest &&
                isGameRunning;

            if (isCurrentlyActive &&
                isGameRunning)
            {
                ShowNotification(
                    "CLOSE THE GAME TO DEACTIVATE",
                    "The active live container cannot be removed safely while Dead as Disco is running.",
                    isError: true);

                _isX19SwitchRequest = false;
                return;
            }

            _isLiveModChangeRunning = true;
            _discordPresenceSwitchTarget =
                selectedMod.DisplayName;

            RefreshDiscordPresence(
                isGameRunning);

            LiveLoaderStatusText.Text =
                isGameRunning
                    ? "SWITCHING"
                    : LiveLoaderStatusText.Text;

            if (isGameRunning)
            {
                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource(
                        "CyanBrush");
            }

            LiveModSwitchingWindow? switchingWindow =
                null;

            X19SwitchPulseWindow? x19PulseWindow =
                null;

            void CloseSwitchingWindow()
            {
                if (switchingWindow is null)
                {
                    return;
                }

                switchingWindow.CloseWhenFinished();
                switchingWindow = null;
            }

            void CloseX19PulseWindow()
            {
                if (x19PulseWindow is null)
                {
                    return;
                }

                x19PulseWindow.CloseWhenFinished();
                x19PulseWindow = null;
            }

            try
            {
                if (isCurrentlyActive)
                {
                    await Task.Run(() =>
                        _modDeploymentService.Deactivate(
                            gameDirectory));

                    _settings.ActiveModId =
                        string.Empty;

                    _settings.PendingDeploymentModId =
                        string.Empty;
                }
                else if (isGameRunning)
                {
                    bool isFirstLiveSwitch =
                        _nextLiveMountOrder == 1000;

                    IntPtr gameWindowHandle =
                        _gameProcessService.FindGameWindow(
                            gameDirectory);

                    if (useX19Pulse &&
                        !isFirstLiveSwitch)
                    {
                        // X19 is meant to feel instant and unobtrusive, so I only
                        // show Limelight's pulsing mark while the switch is moving.
                        x19PulseWindow =
                            new X19SwitchPulseWindow();

                        x19PulseWindow.ShowOverGame(
                            gameWindowHandle);
                    }
                    else
                    {
                        // The first X19 scan can take long enough to make the
                        // game look frozen. I show the full in-game progress card
                        // once, then return to the quiet pulse for later swaps.
                        switchingWindow =
                            new LiveModSwitchingWindow(
                                selectedMod.DisplayName,
                                isFirstLiveSwitch);

                        switchingWindow.ShowOverGame(
                            gameWindowHandle);
                    }

                    if (!_liveLoaderBridgeService.IsOnline())
                    {
                        throw new InvalidOperationException(
                            "The game is running, but Limelight's Live Loader is not online.");
                    }

                    LiveLoaderCommandResult safetyCheck =
                        await WaitForLiveSwitchWindowAsync(
                            (phase, progress) =>
                            {
                                if (x19PulseWindow is not null)
                                {
                                    x19PulseWindow.Report(
                                        progress);
                                }
                                else
                                {
                                    switchingWindow?.Report(
                                        phase,
                                        progress);
                                }
                            });

                    if (!safetyCheck.Success)
                    {
                        if (IsLevelTransitionBlock(
                                safetyCheck.Message))
                        {
                            // I stop before staging or mounting anything while Unreal
                            // is replacing the current world.
                            LevelTransitionBlockerMessage.Text =
                                safetyCheck.Message +
                                " Wait until the new level is fully visible, then select Activate again.";

                            LevelTransitionBlocker.Visibility =
                                Visibility.Visible;

                            if (x19PulseWindow is not null)
                            {
                                x19PulseWindow.ShowError();
                                x19PulseWindow = null;
                            }
                            else if (switchingWindow is not null)
                            {
                                switchingWindow.ShowError(
                                    safetyCheck.Message);

                                // The overlay now owns its timed closing animation.
                                switchingWindow = null;
                            }

                            return;
                        }

                        throw new InvalidOperationException(
                            safetyCheck.Message);
                    }

                    await ActivateLiveModAsync(
                        selectedMod,
                        gameDirectory,
                        (phase, progress) =>
                        {
                            if (x19PulseWindow is not null)
                            {
                                x19PulseWindow.Report(
                                    progress);
                            }
                            else
                            {
                                switchingWindow?.Report(
                                    phase,
                                    progress);
                            }
                        });

                    _settings.ActiveModId =
                        selectedMod.Id;

                    // The live copy is already active. Once the game closes, Limelight
                    // mirrors the same choice into ~mods for the next launch.
                    _settings.PendingDeploymentModId =
                        selectedMod.Id;
                }
                else
                {
                    await Task.Run(() =>
                        _modDeploymentService.Activate(
                            selectedMod,
                            gameDirectory));

                    _settings.ActiveModId =
                        selectedMod.Id;

                    _settings.PendingDeploymentModId =
                        string.Empty;
                }

                _settingsService.Save(
                    _settings);

                RefreshLibrarySummary();

                string notificationTitle =
                    isCurrentlyActive
                        ? "MOD DEACTIVATED"
                        : "MOD ACTIVE";

                string notificationMessage =
                    isCurrentlyActive
                        ? $"{selectedMod.DisplayName} is no longer active."
                        : isGameRunning
                            ? $"{selectedMod.DisplayName} is now active live."
                            : $"{selectedMod.DisplayName} is active and ready for the next launch.";

                if (isGameRunning &&
                    x19PulseWindow is not null)
                {
                    x19PulseWindow.ShowSuccess();
                    x19PulseWindow = null;
                }
                else if (isGameRunning &&
                         switchingWindow is not null)
                {
                    switchingWindow.ShowSuccess(
                        notificationMessage);

                    // The in-game card remains visible briefly and closes itself.
                    switchingWindow = null;
                }
                else
                {
                    ShowNotification(
                        notificationTitle,
                        notificationMessage,
                        isError: false);
                }
            }
            catch (Exception exception)
            {
                if (isGameRunning &&
                    IsLevelTransitionBlock(
                        exception.Message))
                {
                    // If a level change began after the first check, I stop the
                    // remaining stages and explain why nothing else was touched.
                    LevelTransitionBlockerMessage.Text =
                        exception.Message +
                        " Wait until the new level is fully visible, then select Activate again.";

                    LevelTransitionBlocker.Visibility =
                        Visibility.Visible;

                    if (x19PulseWindow is not null)
                    {
                        x19PulseWindow.ShowError();
                        x19PulseWindow = null;
                    }
                    else
                    {
                        CloseSwitchingWindow();
                    }
                }
                else if (isGameRunning &&
                    x19PulseWindow is not null)
                {
                    x19PulseWindow.ShowError();
                    x19PulseWindow = null;
                }
                else if (isGameRunning &&
                         switchingWindow is not null)
                {
                    switchingWindow.ShowError(
                        exception.Message);

                    // Errors remain visible for slightly longer before closing.
                    switchingWindow = null;
                }
                else
                {
                    ShowNotification(
                        "MOD ACTIVATION FAILED",
                        exception.Message,
                        isError: true);
                }
            }
            finally
            {
                CloseSwitchingWindow();
                CloseX19PulseWindow();

                _isLiveModChangeRunning = false;
                _isX19SwitchRequest = false;
                _discordPresenceSwitchTarget =
                    string.Empty;

                UpdateGameRunningStatus();
                RefreshDiscordPresence(
                    isGameRunning);
            }
        }

        private async Task<LiveLoaderCommandResult> WaitForLiveSwitchWindowAsync(
            Action<string, int>? reportProgress)
        {
            DateTime deadline =
                DateTime.UtcNow.AddSeconds(30);

            LiveLoaderCommandResult result =
                await _liveLoaderCommandService
                    .CanSwitchModsAsync();

            int consecutiveReadyChecks = 0;

            while (DateTime.UtcNow < deadline)
            {
                if (result.Success)
                {
                    consecutiveReadyChecks++;

                    // Two clean samples prevent a brief gap between Unreal world
                    // callbacks from opening the switch gate too early.
                    if (consecutiveReadyChecks >= 2)
                    {
                        return result;
                    }

                    reportProgress?.Invoke(
                        "VERIFYING A STABLE GAME WORLD",
                        9);

                    await Task.Delay(250);
                }
                else
                {
                    consecutiveReadyChecks = 0;

                    if (IsLevelTransitionBlock(
                            result.Message))
                    {
                        // A click made during LoadMap is rejected. I do not hold
                        // it in a queue and surprise the user after the map opens.
                        return result;
                    }

                    if (!IsTemporaryLiveSwitchDelay(result.Message))
                    {
                        return result;
                    }

                    reportProgress?.Invoke(
                        "WAITING FOR LIVE ASSETS TO SETTLE",
                        8);

                    await Task.Delay(400);
                }

                result =
                    await _liveLoaderCommandService
                        .CanSwitchModsAsync();
            }

            return result;
        }

        private async Task<LiveLoaderCommandResult> WaitForInitialLiveWorldAsync(
            string gameDirectory,
            Action<string, int>? reportProgress)
        {
            DateTime deadline =
                DateTime.UtcNow.AddMinutes(4);

            LiveLoaderCommandResult result =
                await _liveLoaderCommandService
                    .CanSwitchModsAsync();

            int consecutiveReadyChecks = 0;

            while (DateTime.UtcNow < deadline)
            {
                if (!_gameProcessService.IsGameRunning(
                        gameDirectory))
                {
                    return new LiveLoaderCommandResult
                    {
                        Success = false,
                        Message =
                            "Dead as Disco closed before the first game world was ready."
                    };
                }

                if (result.Success)
                {
                    consecutiveReadyChecks++;

                    // Startup crosses several short-lived Unreal worlds. I wait
                    // for a few clean checks before mounting the saved active mod.
                    if (consecutiveReadyChecks >= 3)
                    {
                        return result;
                    }

                    reportProgress?.Invoke(
                        "VERIFYING THE FIRST GAME WORLD",
                        31);

                    await Task.Delay(350);
                }
                else
                {
                    consecutiveReadyChecks = 0;

                    if (!IsLevelTransitionBlock(result.Message) &&
                        !IsTemporaryLiveSwitchDelay(result.Message))
                    {
                        return result;
                    }

                    // A launch-time transition is expected. Unlike a manual
                    // switch, this request is safe to wait because no user action
                    // has been queued and the active mod has not been touched yet.
                    reportProgress?.Invoke(
                        "WAITING FOR THE FIRST LEVEL",
                        30);

                    await Task.Delay(500);
                }

                result =
                    await _liveLoaderCommandService
                        .CanSwitchModsAsync();
            }

            return new LiveLoaderCommandResult
            {
                Success = false,
                Message =
                    "Dead as Disco did not finish its initial level transition in time."
            };
        }

        private static bool IsTemporaryLiveSwitchDelay(
            string message)
        {
            return ContainsAny(
                message,
                "still settling",
                "still retiring",
                "temporarily locked",
                "level is still loading",
                "world is still loading");
        }

        private static bool IsLevelTransitionBlock(
            string message)
        {
            return ContainsAny(
                message,
                "changing levels",
                "level transition",
                "level is still loading",
                "world is still loading",
                "loadmap");
        }

        private static bool ContainsAny(
            string value,
            params string[] candidates)
        {
            return candidates.Any(candidate =>
                value.Contains(
                    candidate,
                    StringComparison.OrdinalIgnoreCase));
        }

        private LimelightDialogChoice ShowLimelightDialog(
            string heading,
            string message,
            LimelightDialogTone tone = LimelightDialogTone.Information,
            string primaryAction = "OK",
            string? secondaryAction = null,
            string? details = null,
            string? eyebrow = null,
            string? footerHint = null,
            bool showCancel = false)
        {
            // Keeping the owner here makes every prompt stay with Limelight,
            // including when the main window is moved to another monitor.
            return LimelightDialog.Open(
                this,
                heading,
                message,
                tone,
                primaryAction,
                secondaryAction,
                details,
                eyebrow,
                footerHint,
                showCancel);
        }

        private async void ShowNotification(
            string title,
            string message,
            bool isError)
        {
            int sequence =
                ++_notificationSequence;

            Brush statusBrush =
                (Brush)FindResource(
                    isError
                        ? "PinkBrush"
                        : "CyanBrush");

            NotificationToastTitle.Text =
                title.ToUpperInvariant();

            NotificationToastMessage.Text =
                message;

            NotificationToastAccent.Background =
                statusBrush;

            NotificationToastTitle.Foreground =
                statusBrush;

            NotificationToastIcon.Foreground =
                statusBrush;

            NotificationToastIcon.Text =
                isError
                    ? "!"
                    : "◆";

            // Clear an older animation first so a new message appears at full
            // strength even when the previous toast was fading away.
            NotificationToast.BeginAnimation(
                OpacityProperty,
                null);

            NotificationToastTransform.BeginAnimation(
                TranslateTransform.YProperty,
                null);

            NotificationToast.Opacity = 0;
            NotificationToastTransform.Y = 16;
            NotificationToast.Visibility =
                Visibility.Visible;

            var entranceEase =
                new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                };

            NotificationToast.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(
                    0,
                    1,
                    TimeSpan.FromMilliseconds(180)));

            NotificationToastTransform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(
                    16,
                    0,
                    TimeSpan.FromMilliseconds(230))
                {
                    EasingFunction = entranceEase
                });

            await Task.Delay(
                isError
                    ? 6500
                    : 4200);

            if (sequence != _notificationSequence ||
                !IsLoaded)
            {
                return;
            }

            NotificationToast.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(
                    1,
                    0,
                    TimeSpan.FromMilliseconds(220)));

            NotificationToastTransform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(
                    0,
                    10,
                    TimeSpan.FromMilliseconds(220)));

            await Task.Delay(230);

            if (sequence == _notificationSequence &&
                IsLoaded)
            {
                NotificationToast.Visibility =
                    Visibility.Collapsed;
            }
        }

        private async void RemoveModRequested(string modId)
        {
            InstalledMod? selectedMod =
                _settings.InstalledMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        modId,
                        StringComparison.OrdinalIgnoreCase));

            if (selectedMod == null)
            {
                return;
            }

            LimelightDialogChoice confirmation =
                ShowLimelightDialog(
                    "REMOVE THIS MOD?",
                    $"Remove {selectedMod.DisplayName} from Limelight? This deletes Limelight's stored copy of the mod.",
                    LimelightDialogTone.Question,
                    primaryAction: "REMOVE MOD",
                    secondaryAction: "KEEP MOD",
                    eyebrow: "LIBRARY CHANGE");

            if (confirmation != LimelightDialogChoice.Primary)
            {
                return;
            }

            bool isCurrentlyActive =
                string.Equals(
                    _settings.ActiveModId,
                    selectedMod.Id,
                    StringComparison.OrdinalIgnoreCase);

            if (isCurrentlyActive &&
                !string.IsNullOrWhiteSpace(_gameDirectory) &&
                _gameProcessService.IsGameRunning(
                    _gameDirectory))
            {
                ShowLimelightDialog(
                    "ACTIVE MOD IS IN USE",
                    "Close Dead as Disco before removing the active mod from Limelight.",
                    LimelightDialogTone.Warning,
                    eyebrow: "REMOVE BLOCKED");

                return;
            }

            try
            {
                // Deactivate first so removing an active library copy never
                // leaves its managed packages inside the game directory.
                if (isCurrentlyActive)
                {
                    if (string.IsNullOrWhiteSpace(_gameDirectory))
                    {
                        throw new InvalidOperationException(
                            "Reconnect the game before removing the active mod.");
                    }

                    string gameDirectory =
                        _gameDirectory;

                    await Task.Run(() =>
                        _modDeploymentService.Deactivate(
                            gameDirectory));

                    _settings.ActiveModId =
                        string.Empty;
                }

                await Task.Run(() =>
                {
                    if (Directory.Exists(
                            selectedMod.InstallDirectory))
                    {
                        Directory.Delete(
                            selectedMod.InstallDirectory,
                            recursive: true);
                    }
                });

                _settings.InstalledMods.Remove(
                    selectedMod);

                if (string.Equals(
                        _settings.PendingDeploymentModId,
                        selectedMod.Id,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _settings.PendingDeploymentModId =
                        string.Empty;
                }

                _settingsService.Save(_settings);
                RefreshLibrarySummary();
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "MOD COULD NOT BE REMOVED",
                    "Limelight kept the library entry so nothing is lost.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "REMOVE FAILED");
            }
        }

        private void RenameModRequested(
            string modId,
            string displayName)
        {
            InstalledMod? selectedMod =
                _settings.InstalledMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        modId,
                        StringComparison.OrdinalIgnoreCase));

            if (selectedMod is null)
            {
                return;
            }

            string cleanedName =
                string.Join(
                    " ",
                    displayName
                        .Split(
                            ' ',
                            StringSplitOptions.RemoveEmptyEntries))
                .Trim();

            if (cleanedName.Length == 0)
            {
                return;
            }

            selectedMod.CustomDisplayName =
                cleanedName;

            _settingsService.Save(_settings);
            RefreshLibrarySummary();

            ShowNotification(
                "MOD RENAMED",
                $"{selectedMod.DisplayName} is now shown with its new name.",
                isError: false);
        }

        private void ShowMyMods_Click(
    object sender,
    MouseButtonEventArgs e)
        {
            ShowMyModsPage();
        }

        private void ShowMyModsPage()
        {
            // Refresh before displaying the page so newly imported
            // mods appear without restarting Limelight.
            RefreshLibrarySummary();

            DashboardPage.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Collapsed;

            DownloadsPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            LiveLoadersPageControl.Visibility =
                Visibility.Collapsed;

            ProfilesPageControl.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Visible;

            SetSelectedNavigation(showMyMods: true);
        }

        private void ShowProfiles_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            ShowProfilesPage();
        }

        private void ShowProfilesPage()
        {
            RefreshLibrarySummary();

            DashboardPage.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            LiveLoadersPageControl.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Collapsed;

            DownloadsPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            ProfilesPageControl.Visibility =
                Visibility.Visible;

            _selectedNavigationPage =
                NavigationPage.Profiles;

            ApplyNavigationAppearance();
            RefreshDiscordPresence();
        }

        private void ShowLiveLoaders_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            ShowLiveLoadersPage();
        }

        private void ShowLiveLoadersPage()
        {
            // I refresh first so imported or removed mods are immediately
            // reflected in the user's X19 rotation.
            RefreshLibrarySummary();

            DashboardPage.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Collapsed;

            DownloadsPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            ProfilesPageControl.Visibility =
                Visibility.Collapsed;

            LiveLoadersPageControl.Visibility =
                Visibility.Visible;

            _selectedNavigationPage =
                NavigationPage.LiveLoaders;

            ApplyNavigationAppearance();
            RefreshDiscordPresence();
        }

        private void ProfilesChanged(
            IReadOnlyList<ModProfile> profiles)
        {
            HashSet<string> oldGroupedModIds =
                _settings.ModProfiles
                    .Where(profile =>
                        _settings.X19LoaderProfileIds.Contains(
                            profile.Id,
                            StringComparer.OrdinalIgnoreCase))
                    .SelectMany(profile => profile.ModIds)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<string> standaloneModIds =
                _settings.X19LoaderModIds
                    .Where(modId => !oldGroupedModIds.Contains(modId))
                    .ToList();

            // I replace the saved snapshot in one step so a half-edited
            // profile can never leak into the X19 rotation.
            _settings.ModProfiles =
                profiles
                    .Select(profile =>
                        new ModProfile
                        {
                            Id = profile.Id,
                            Name = profile.Name,
                            ModIds = profile.ModIds
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList(),
                            CreatedAt = profile.CreatedAt,
                            UpdatedAt = profile.UpdatedAt
                        })
                    .ToList();

            HashSet<string> availableProfileIds =
                _settings.ModProfiles
                    .Select(profile => profile.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _settings.X19LoaderProfileIds =
                _settings.X19LoaderProfileIds
                    .Where(availableProfileIds.Contains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            IEnumerable<string> refreshedGroupedModIds =
                _settings.ModProfiles
                    .Where(profile =>
                        _settings.X19LoaderProfileIds.Contains(
                            profile.Id,
                            StringComparer.OrdinalIgnoreCase))
                    .SelectMany(profile => profile.ModIds);

            _settings.X19LoaderModIds =
                refreshedGroupedModIds
                    .Concat(standaloneModIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            _settingsService.Save(_settings);
        }

        private void UseProfileInX19Requested(
            string profileId)
        {
            ModProfile? profile =
                _settings.ModProfiles.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.Id,
                        profileId,
                        StringComparison.OrdinalIgnoreCase));

            if (profile is null)
            {
                return;
            }

            HashSet<string> availableIds =
                _settings.InstalledMods
                    .Where(mod => Directory.Exists(mod.InstallDirectory))
                    .Select(mod => mod.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<string> rotationIds =
                profile.ModIds
                    .Where(availableIds.Contains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (rotationIds.Count == 0)
            {
                ShowNotification(
                    "PROFILE NEEDS AVAILABLE MODS",
                    $"None of the characters saved in {profile.Name} are currently available.",
                    isError: true);

                return;
            }

            _settings.X19LoaderModIds =
                rotationIds;

            _settings.X19LoaderProfileIds =
                new List<string>
                {
                    profile.Id
                };

            _settingsService.Save(_settings);
            ShowLiveLoadersPage();

            ShowNotification(
                "X19 PROFILE READY",
                $"{profile.Name} replaced the current X19 rotation.",
                isError: false);
        }

        private void X19GroupChanged(
            IReadOnlyList<string> selectedModIds)
        {
            // I remove duplicates before saving so every hotkey press advances
            // through one predictable copy of each selected character.
            _settings.X19LoaderModIds =
                selectedModIds
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            _settingsService.Save(_settings);
        }

        private void X19ProfileGroupsChanged(
            IReadOnlyList<string> selectedProfileIds)
        {
            _settings.X19LoaderProfileIds =
                selectedProfileIds
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            _settingsService.Save(_settings);
        }

        private void X19ShuffleChanged(
            bool shuffleEnabled)
        {
            _settings.X19ShuffleEnabled =
                shuffleEnabled;

            _settingsService.Save(_settings);
        }

        private void X19HotkeyChanged(
            string hotkeyGesture)
        {
            _settings.X19HotkeyGesture =
                hotkeyGesture;

            _settingsService.Save(_settings);

            if (_selectedLoaderMode ==
                LoaderLaunchMode.X19)
            {
                EnableX19Hotkey();
            }

            // I refresh the loader page too so its hotkey badge changes
            // immediately instead of waiting for another navigation visit.
            RefreshLibrarySummary();
        }

        private void ResourceOverlayChanged(
    bool enabled)
        {
            _settings.ResourceOverlayEnabled =
                enabled;

            _settingsService.Save(_settings);

            ApplyResourceOverlayPreference();

            SettingsPageControl.ShowResourceOverlay(
                enabled);
        }

        private void ApplyResourceOverlayPreference()
        {
            if (_settings.ResourceOverlayEnabled)
            {
                if (_resourceUsageOverlayWindow != null)
                {
                    return;
                }

                _resourceUsageOverlayWindow =
                    new ResourceUsageOverlayWindow();

                _resourceUsageOverlayWindow.Closed +=
                    ResourceUsageOverlayWindow_Closed;

                _resourceUsageOverlayWindow.Show();

                return;
            }

            _resourceUsageOverlayWindow?.Close();
            _resourceUsageOverlayWindow = null;
        }

        private void ResourceUsageOverlayWindow_Closed(
            object? sender,
            EventArgs e)
        {
            _resourceUsageOverlayWindow = null;
        }

        private void DiscordPresenceChanged(
            bool enabled)
        {
            _settings.DiscordRichPresenceEnabled =
                enabled;

            _settingsService.Save(
                _settings);

            _discordPresenceService.SetEnabled(
                enabled);

            SettingsPageControl.ShowDiscordPresence(
                enabled);

            RefreshDiscordPresence();

            ShowNotification(
                enabled
                    ? "DISCORD PRESENCE ENABLED"
                    : "DISCORD PRESENCE DISABLED",
                enabled
                    ? "Limelight will now share its current activity through the Discord desktop client."
                    : "Limelight cleared its Discord activity and returned to private mode.",
                isError: false);
        }

        private async void TestLiveLoader_Click(
    object sender,
    RoutedEventArgs e)
        {
            if (!_liveLoaderBridgeService.IsOnline())
            {
                ShowLimelightDialog(
                    "LIVE LOADER IS OFFLINE",
                    "Start Dead as Disco and wait for the Live Loader status to show ONLINE.",
                    LimelightDialogTone.Information,
                    eyebrow: "NATIVE TEST");

                return;
            }

            LiveLoaderStatusText.Text =
                "CHECKING";

            LiveLoaderStatusText.Foreground =
                (Brush)FindResource("CyanBrush");

            try
            {
                // Ask the native half of the bridge directly instead of assuming it
                // loaded just because the Lua heartbeat is alive.
                LiveLoaderCommandResult result =
                    await _liveLoaderCommandService.PingNativeAsync();

                LiveLoaderStatusText.Text =
                    result.Success
                        ? "ONLINE"
                        : "NATIVE OFFLINE";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource(
                        result.Success
                            ? "CyanBrush"
                            : "PinkBrush");

                ShowLimelightDialog(
                    result.Success
                        ? "NATIVE BRIDGE ONLINE"
                        : "NATIVE BRIDGE UNAVAILABLE",
                    result.Message,
                    result.Success
                        ? LimelightDialogTone.Success
                        : LimelightDialogTone.Warning,
                    eyebrow: "NATIVE TEST");
            }
            catch (Exception exception)
            {
                LiveLoaderStatusText.Text =
                    "TEST FAILED";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("PinkBrush");

                ShowLimelightDialog(
                    "NATIVE BRIDGE TEST FAILED",
                    "Limelight could not contact its native bridge.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "NATIVE TEST");
            }
        }

        private void ShowDashboard_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            ShowDashboardPage();
        }

        private void ShowDashboardPage()
        {
            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            ProfilesPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Collapsed;

            DownloadsPageControl.Visibility =
                Visibility.Collapsed;

            LiveLoadersPageControl.Visibility =
                Visibility.Collapsed;

            DashboardPage.Visibility =
                Visibility.Visible;

            SetSelectedNavigation(showMyMods: false);
        }

        private void ShowSettings_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            ShowSettingsPage();
        }

        private void ShowSettingsPage()
        {
            DashboardPage.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Collapsed;

            DownloadsPageControl.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            ProfilesPageControl.Visibility =
                Visibility.Collapsed;

            LiveLoadersPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Visible;

            RefreshSettingsPage();
            SetSelectedNavigation(
                showMyMods: false,
                showSettings: true);
        }

        private void ShowDownloads_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            ShowDownloadsPage();
        }

        private void ShowDownloadsPage()
        {
            DashboardPage.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            ProfilesPageControl.Visibility =
                Visibility.Collapsed;

            LiveLoadersPageControl.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            DownloadsPageControl.Visibility =
                Visibility.Visible;

            RefreshDownloadsPage();

            _selectedNavigationPage =
                NavigationPage.Downloads;

            ApplyNavigationAppearance();
            RefreshDiscordPresence();
        }

        private void ClearFinishedDownloadsRequested()
        {
            _downloadHistoryService.ClearFinished();
            RefreshDownloadsPage();
        }

        private void RefreshDownloadsPage()
        {
            DownloadsPageControl.ShowDownloads(
                _downloadHistoryService.Records);
        }

        private void SetSelectedNavigation(
    bool showMyMods,
    bool showSettings = false,
    bool showBrowseNexus = false)
        {
            _selectedNavigationPage =
                showSettings
                    ? NavigationPage.Settings
                    : showBrowseNexus
                        ? NavigationPage.BrowseNexus
                        : showMyMods
                            ? NavigationPage.MyMods
                            : NavigationPage.Dashboard;

            ApplyNavigationAppearance();
            RefreshDiscordPresence();
        }

        private void ApplyNavigationAppearance()
        {
            // The icon is kept separate from the label so the selected page
            // can fill its diamond without moving the text beside it.
            ApplyNavigationItemAppearance(
                DashboardNavigation,
                DashboardNavigationIcon,
                DashboardNavigationText,
                _selectedNavigationPage == NavigationPage.Dashboard);

            ApplyNavigationItemAppearance(
                MyModsNavigation,
                MyModsNavigationIcon,
                MyModsNavigationText,
                _selectedNavigationPage == NavigationPage.MyMods);

            ApplyNavigationItemAppearance(
                ProfilesNavigation,
                ProfilesNavigationIcon,
                ProfilesNavigationText,
                _selectedNavigationPage == NavigationPage.Profiles);

            ApplyNavigationItemAppearance(
                LiveLoadersNavigation,
                LiveLoadersNavigationIcon,
                LiveLoadersNavigationText,
                _selectedNavigationPage == NavigationPage.LiveLoaders);

            ApplyNavigationItemAppearance(
                BrowseNexusNavigation,
                BrowseNexusNavigationIcon,
                BrowseNexusNavigationText,
                _selectedNavigationPage == NavigationPage.BrowseNexus);

            ApplyNavigationItemAppearance(
                DownloadsNavigation,
                DownloadsNavigationIcon,
                DownloadsNavigationText,
                _selectedNavigationPage == NavigationPage.Downloads);

            ApplyNavigationItemAppearance(
                SettingsNavigation,
                SettingsNavigationIcon,
                SettingsNavigationText,
                _selectedNavigationPage == NavigationPage.Settings);
        }

        private void ApplyNavigationItemAppearance(
            Border navigation,
            TextBlock icon,
            TextBlock label,
            bool isSelected)
        {
            Brush pink =
                (Brush)FindResource("PinkBrush");

            Brush normalText =
                (Brush)FindResource("TextBrush");

            Brush mutedText =
                (Brush)FindResource("MutedTextBrush");

            navigation.Background =
                isSelected
                    ? new SolidColorBrush(
                        Color.FromRgb(37, 32, 59))
                    : Brushes.Transparent;

            navigation.BorderBrush =
                isSelected
                    ? pink
                    : Brushes.Transparent;

            icon.Text =
                isSelected
                    ? "◆"
                    : "◇";

            icon.Foreground =
                isSelected
                    ? pink
                    : mutedText;

            label.Foreground =
                isSelected
                    ? normalText
                    : mutedText;
        }

        private void Navigation_MouseEnter(
            object sender,
            MouseEventArgs e)
        {
            if (sender is not Border navigation ||
                IsSelectedNavigation(navigation))
            {
                return;
            }

            // Hover uses a neutral grey panel and keeps the diamond hollow.
            // The pink filled icon is reserved for the page that is open.
            navigation.Background =
                new SolidColorBrush(
                    Color.FromRgb(27, 30, 43));

            GetNavigationParts(
                navigation,
                out TextBlock? icon,
                out TextBlock? label);

            if (icon is not null)
            {
                icon.Text = "◇";
                icon.Foreground =
                    (Brush)FindResource("MutedTextBrush");
            }

            if (label is not null)
            {
                label.Foreground =
                    (Brush)FindResource("TextBrush");
            }
        }

        private void Navigation_MouseLeave(
            object sender,
            MouseEventArgs e)
        {
            ApplyNavigationAppearance();
        }

        private bool IsSelectedNavigation(
            Border navigation)
        {
            return
                (navigation == DashboardNavigation &&
                 _selectedNavigationPage == NavigationPage.Dashboard) ||
                (navigation == MyModsNavigation &&
                 _selectedNavigationPage == NavigationPage.MyMods) ||
                (navigation == ProfilesNavigation &&
                 _selectedNavigationPage == NavigationPage.Profiles) ||
                (navigation == LiveLoadersNavigation &&
                 _selectedNavigationPage == NavigationPage.LiveLoaders) ||
                (navigation == DownloadsNavigation &&
                 _selectedNavigationPage == NavigationPage.Downloads) ||
                (navigation == SettingsNavigation &&
                 _selectedNavigationPage == NavigationPage.Settings) ||
                (navigation == BrowseNexusNavigation &&
                 _selectedNavigationPage == NavigationPage.BrowseNexus);
        }

        private void GetNavigationParts(
            Border navigation,
            out TextBlock? icon,
            out TextBlock? label)
        {
            if (navigation == DashboardNavigation)
            {
                icon = DashboardNavigationIcon;
                label = DashboardNavigationText;
                return;
            }

            if (navigation == MyModsNavigation)
            {
                icon = MyModsNavigationIcon;
                label = MyModsNavigationText;
                return;
            }

            if (navigation == ProfilesNavigation)
            {
                icon = ProfilesNavigationIcon;
                label = ProfilesNavigationText;
                return;
            }

            if (navigation == LiveLoadersNavigation)
            {
                icon = LiveLoadersNavigationIcon;
                label = LiveLoadersNavigationText;
                return;
            }

            if (navigation == BrowseNexusNavigation)
            {
                icon = BrowseNexusNavigationIcon;
                label = BrowseNexusNavigationText;
                return;
            }

            if (navigation == DownloadsNavigation)
            {
                icon = DownloadsNavigationIcon;
                label = DownloadsNavigationText;
                return;
            }

            if (navigation == SettingsNavigation)
            {
                icon = SettingsNavigationIcon;
                label = SettingsNavigationText;
                return;
            }

            icon = null;
            label = null;
        }

        private async void NexusConnectRequested(
    string apiKey)
        {
            if (!NexusApiService.IntegrationEnabled)
            {
                SettingsPageControl.ShowNexusUnavailable();
                return;
            }

            await ConnectNexusAsync(
                apiKey,
                isRestoring: false);
        }

        private void NexusDisconnectRequested()
        {
            if (!NexusApiService.IntegrationEnabled)
            {
                // The approval gate is not a disconnect request. Keeping the
                // protected value means the user does not need to enter it again later.
                SettingsPageControl.ShowNexusUnavailable();
                return;
            }

            ClearNexusCredentials();

            SettingsPageControl.ShowNexusStatus(
                isConnected: false,
                accountName: null);

            ShowNotification(
                "NEXUS DISCONNECTED",
                "Your Nexus Mods account has been disconnected from Limelight.",
                isError: false);
        }

        private async Task RestoreNexusConnectionAsync()
        {
            if (!NexusApiService.IntegrationEnabled)
            {
                SettingsPageControl.ShowNexusUnavailable();
                return;
            }

            string apiKey =
                _nexusCredentialService.Unprotect(
                    _settings.ProtectedNexusApiKey);

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                // A protected value that cannot be opened probably came from
                // another Windows account or a damaged settings file.
                if (!string.IsNullOrWhiteSpace(
                        _settings.ProtectedNexusApiKey))
                {
                    ClearNexusCredentials();
                }

                SettingsPageControl.ShowNexusStatus(
                    isConnected: false,
                    accountName: null);

                return;
            }

            await ConnectNexusAsync(
                apiKey,
                isRestoring: true);
        }

        private async Task ConnectNexusAsync(
            string apiKey,
            bool isRestoring)
        {
            if (!NexusApiService.IntegrationEnabled)
            {
                SettingsPageControl.ShowNexusUnavailable();
                return;
            }

            SettingsPageControl.ShowNexusStatus(
                isConnected: false,
                accountName: null,
                isBusy: true);

            try
            {
                NexusAccount account =
                    await _nexusApiService.ValidateApiKeyAsync(
                        apiKey);

                _nexusApiKey =
                    apiKey.Trim();

                _nexusAccount =
                    account;

                _settings.ProtectedNexusApiKey =
                    _nexusCredentialService.Protect(
                        _nexusApiKey);

                _settings.NexusAccountName =
                    account.Name;

                _settingsService.Save(
                    _settings);

                SettingsPageControl.ShowNexusStatus(
                    isConnected: true,
                    accountName: CreateNexusAccountLabel(account));

                if (!isRestoring)
                {
                    ShowNotification(
                        "NEXUS CONNECTED",
                        $"{account.Name} is now connected to Limelight.",
                        isError: false);
                }
            }
            catch (UnauthorizedAccessException exception)
            {
                _nexusApiKey =
                    string.Empty;

                _nexusAccount =
                    null;

                if (isRestoring)
                {
                    ClearNexusCredentials();
                }

                SettingsPageControl.ShowNexusStatus(
                    isConnected: false,
                    accountName: null);

                if (!isRestoring)
                {
                    ShowNotification(
                        "NEXUS CONNECTION FAILED",
                        exception.Message,
                        isError: true);
                }
            }
            catch (Exception exception)
            {
                _nexusApiKey =
                    string.Empty;

                _nexusAccount =
                    null;

                // A temporary network failure should not erase a previously
                // accepted key. Limelight can try it again next time it opens.
                SettingsPageControl.ShowNexusStatus(
                    isConnected: false,
                    accountName: null);

                if (!isRestoring)
                {
                    ShowNotification(
                        "NEXUS IS UNAVAILABLE",
                        exception.Message,
                        isError: true);
                }
            }
        }

        private void ClearNexusCredentials()
        {
            _nexusApiKey =
                string.Empty;

            _nexusAccount =
                null;

            _settings.ProtectedNexusApiKey =
                string.Empty;

            _settings.NexusAccountName =
                string.Empty;

            _settingsService.Save(
                _settings);
        }

        private async void NexusViewModRequested(
            NexusModSummary mod)
        {
            if (!NexusApiService.IntegrationEnabled)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_nexusApiKey))
            {
                BrowseNexusPageControl.ShowModDetailsError(
                    "Connect your Nexus Mods account in Settings before opening a mod page.");

                return;
            }

            try
            {
                // Catalogue cards are deliberately light. This request brings in
                // the author's complete description only when somebody opens it.
                NexusModSummary fullMod =
                    await _nexusApiService.GetModAsync(
                        _nexusApiKey,
                        mod.ModId);

                BrowseNexusPageControl.ShowModDetails(
                    fullMod);
            }
            catch (Exception ex)
            {
                BrowseNexusPageControl.ShowModDetailsError(
                    ex.Message);
            }
        }

        private async void NexusViewFilesRequested(
            NexusModSummary mod)
        {
            if (!NexusApiService.IntegrationEnabled)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_nexusApiKey))
            {
                BrowseNexusPageControl.ShowModFilesError(
                    "Connect your Nexus Mods account in Settings before loading files.");

                return;
            }

            try
            {
                IReadOnlyList<NexusModFile> files =
                    await _nexusApiService.GetModFilesAsync(
                        _nexusApiKey,
                        mod.ModId);

                BrowseNexusPageControl.ShowModFiles(
                    mod,
                    files);
            }
            catch (Exception ex)
            {
                BrowseNexusPageControl.ShowModFilesError(
                    ex.Message);
            }
        }

        private async void NexusDownloadRequested(
            NexusModFile file)
        {
            if (!NexusApiService.IntegrationEnabled)
            {
                ShowNotification(
                    "NEXUS APPROVAL PENDING",
                    NexusApiService.IntegrationUnavailableMessage,
                    isError: true);
                return;
            }

            if (_isNexusDownloadRunning)
            {
                ShowNotification(
                    "DOWNLOAD IN PROGRESS",
                    "Let the current Nexus file finish before starting another.",
                    isError: true);

                return;
            }

            if (_nexusAccount is null ||
                string.IsNullOrWhiteSpace(_nexusApiKey))
            {
                ShowNotification(
                    "NEXUS ACCOUNT REQUIRED",
                    "Connect Nexus Mods in Settings before downloading a file.",
                    isError: true);

                return;
            }

            NexusModSummary? selectedMod =
                _nexusBrowseMods.FirstOrDefault(mod =>
                    mod.ModId == file.ModId);

            string displayName =
                selectedMod?.Name ??
                file.FileName;

            bool isAlreadyInstalled =
                _settings.InstalledMods.Any(mod =>
                    (mod.NexusModId == file.ModId &&
                     mod.NexusFileId == file.FileId) ||
                    string.Equals(
                        InstalledMod.CreateDisplayName(
                            mod.Name),
                        InstalledMod.CreateDisplayName(displayName),
                        StringComparison.OrdinalIgnoreCase));

            if (isAlreadyInstalled)
            {
                ShowNotification(
                    "MOD ALREADY INSTALLED",
                    $"{displayName} is already in your Limelight library.",
                    isError: true);

                return;
            }

            _isNexusDownloadRunning =
                true;

            NexusDownloadRecord downloadRecord =
                _downloadHistoryService.Begin(
                    file,
                    displayName);

            RefreshDownloadsPage();

            string downloadedArchive =
                string.Empty;

            try
            {
                BrowseNexusPageControl.ShowDownloadState(
                    file,
                    "REQUESTING A SECURE NEXUS DOWNLOAD",
                    isBusy: true);

                int lastShownPercentage =
                    -1;

                long lastShownBytes =
                    0;

                var progress =
                    new Progress<NexusDownloadProgress>(snapshot =>
                    {
                        _downloadHistoryService.ReportProgress(
                            downloadRecord.Id,
                            snapshot);

                        bool shouldRefresh =
                            snapshot.TotalBytes is > 0
                                ? snapshot.Percentage != lastShownPercentage
                                : lastShownBytes == 0 ||
                                    snapshot.BytesReceived - lastShownBytes >=
                                        1024L * 1024L;

                        if (!shouldRefresh)
                        {
                            return;
                        }

                        lastShownPercentage =
                            snapshot.Percentage;

                        lastShownBytes =
                            snapshot.BytesReceived;

                        BrowseNexusPageControl.ShowDownloadState(
                            file,
                            "DOWNLOADING AND CHECKING THE ARCHIVE",
                            isBusy: true,
                            snapshot.TotalBytes is > 0
                                ? snapshot.Percentage
                                : null);

                        RefreshDownloadsPage();
                    });

                downloadedArchive =
                    await _nexusApiService.DownloadModFileAsync(
                        _nexusApiKey,
                        file,
                        progress);

                _downloadHistoryService.MarkInstalling(
                    downloadRecord.Id);

                RefreshDownloadsPage();

                BrowseNexusPageControl.ShowDownloadState(
                    file,
                    "VALIDATING AND INSTALLING THE MOD",
                    isBusy: true,
                    percentage: 100);

                InstalledMod installedMod =
                    await Task.Run(() =>
                        _modLibraryService.Import(
                            downloadedArchive,
                            file.ModId,
                            file.FileId,
                            displayName));

                _settings.InstalledMods.Add(
                    installedMod);

                _settingsService.Save(
                    _settings);

                _downloadHistoryService.MarkCompleted(
                    downloadRecord.Id,
                    installedMod);

                RefreshLibrarySummary();
                RefreshDownloadsPage();

                BrowseNexusPageControl.ShowDownloadState(
                    file,
                    $"{installedMod.DisplayName} IS READY IN MY MODS.",
                    isBusy: false);

                ShowNotification(
                    "MOD INSTALLED",
                    $"{installedMod.DisplayName} is ready in My Mods.",
                    isError: false);
            }
            catch (Exception exception)
            {
                _downloadHistoryService.MarkFailed(
                    downloadRecord.Id,
                    exception.Message);

                RefreshDownloadsPage();

                BrowseNexusPageControl.ShowDownloadState(
                    file,
                    "THE DOWNLOAD COULD NOT BE INSTALLED.",
                    isBusy: false);

                ShowNotification(
                    "DOWNLOAD FAILED",
                    exception.Message,
                    isError: true);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(downloadedArchive))
                {
                    NexusApiService.DeleteDownloadedArchive(
                        downloadedArchive);
                }

                _isNexusDownloadRunning =
                    false;
            }
        }
        private static string CreateNexusAccountLabel(
            NexusAccount account)
        {
            if (account.IsPremium)
            {
                return $"{account.Name} (Premium)";
            }

            if (account.IsSupporter)
            {
                return $"{account.Name} (Supporter)";
            }

            return account.Name;
        }
        private void RefreshSettingsPage()
        {
            string? gameDirectory =
                _gameDirectory;

            bool isGameRunning =
                !string.IsNullOrWhiteSpace(gameDirectory) &&
                _gameProcessService.IsGameRunning(
                    gameDirectory);

            LiveSessionState session =
                _liveSessionService.Load();

            LiveSessionCleanupResult stagingSnapshot =
                string.IsNullOrWhiteSpace(gameDirectory)
                    ? new LiveSessionCleanupResult()
                    : _liveSessionService.GetStagingSnapshot(
                        gameDirectory);

            SettingsPageControl.ShowStatus(
                gameDirectory,
                isGameRunning,
                session,
                stagingSnapshot);

            ShowSettingsCompatibility(
                gameDirectory);

            SettingsPageControl.ShowDiscordPresence(
                _settings.DiscordRichPresenceEnabled);

            SettingsPageControl.ShowResourceOverlay(
                _settings.ResourceOverlayEnabled);
        }

        private void ShowSettingsCompatibility(
            string? gameDirectory)
        {
            SettingsPageControl.ShowCompatibility(
                _compatibilityService.Check(
                    gameDirectory));
        }

        private void RefreshDiscordPresence(
            bool? knownGameRunning = null)
        {
            bool isGameRunning =
                knownGameRunning ??
                (!string.IsNullOrWhiteSpace(_gameDirectory) &&
                 _gameProcessService.IsGameRunning(
                     _gameDirectory));

            InstalledMod? activeMod =
                _settings.InstalledMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        _settings.ActiveModId,
                        StringComparison.OrdinalIgnoreCase));

            string pageLabel =
                _selectedNavigationPage switch
                {
                    NavigationPage.Dashboard =>
                        "Managing the Limelight dashboard",
                    NavigationPage.MyMods =>
                        "Browsing character mods",
                    NavigationPage.Profiles =>
                        "Building character profiles",
                    NavigationPage.LiveLoaders =>
                        "Configuring the Live Loader",
                    NavigationPage.BrowseNexus =>
                        "Browsing Nexus Mods",
                    NavigationPage.Downloads =>
                        "Checking mod downloads",
                    NavigationPage.Settings =>
                        "Adjusting Limelight settings",
                    _ =>
                        "Managing Dead as Disco mods"
                };

            string loaderMode =
                _selectedLoaderMode switch
                {
                    LoaderLaunchMode.X19 =>
                        "X19 LLoader",
                    LoaderLaunchMode.Disabled =>
                        "No Live Loader",
                    _ =>
                        "Live Loader"
                };

            _discordPresenceService.Update(
                isGameRunning,
                _isLiveModChangeRunning,
                pageLabel,
                activeMod?.DisplayName,
                loaderMode,
                _discordPresenceSwitchTarget);
        }

        private async void RepairLiveLoaderRequested()
        {
            if (string.IsNullOrWhiteSpace(_gameDirectory))
            {
                ShowLimelightDialog(
                    "GAME NOT CONNECTED",
                    "Connect Limelight to Dead as Disco before repairing the Live Loader.",
                    LimelightDialogTone.Warning,
                    eyebrow: "REPAIR BLOCKED");

                return;
            }

            string gameDirectory =
                _gameDirectory;

            if (_gameProcessService.IsGameRunning(gameDirectory))
            {
                ShowLimelightDialog(
                    "CLOSE THE GAME FIRST",
                    "Dead as Disco must be closed before Limelight can repair the Live Loader.",
                    LimelightDialogTone.Warning,
                    eyebrow: "REPAIR BLOCKED");

                return;
            }

            LocalCompatibilityResult compatibility =
                _compatibilityService.Check(
                    gameDirectory);

            if (!compatibility.GameBuildDetected ||
                !compatibility.GameBuildCompatible)
            {
                ShowLimelightDialog(
                    "UNSUPPORTED GAME BUILD",
                    "Limelight will not install version-sensitive Live Loader files into this game build.",
                    LimelightDialogTone.Warning,
                    details: compatibility.Detail,
                    eyebrow: "COMPATIBILITY GATE");

                return;
            }

            LimelightDialogChoice confirmation =
                ShowLimelightDialog(
                    "REPAIR THE LIVE LOADER?",
                    "This clears stale staging files, refreshes the Dead as Disco configuration, and reinstalls Limelight's bridge. Imported mods are not removed.",
                    LimelightDialogTone.Question,
                    primaryAction: "START REPAIR",
                    secondaryAction: "NOT NOW",
                    eyebrow: "RECOVERY TOOLS");

            if (confirmation != LimelightDialogChoice.Primary)
            {
                return;
            }

            try
            {
                LiveSessionCleanupResult cleanup =
                    await Task.Run(() =>
                        _liveSessionService.RepairClosedSession(
                            gameDirectory));

                Ue4ssDetectionResult loader =
                    _ue4ssDetectionService.Detect(
                        gameDirectory);

                if (!loader.IsInstalled ||
                    !_ue4ssConfigurationService.IsRuntimeCompatible(loader))
                {
                    // The normal setup flow already knows how to fetch the
                    // verified build, so let it handle a missing runtime too.
                    _hasHandledLiveLoaderPrompt = false;
                    _settings.DismissedLiveLoaderPromptForGameDirectory =
                        string.Empty;

                    _settingsService.Save(_settings);
                    await ShowLiveLoaderSetupPromptIfNeeded();
                    RefreshSettingsPage();
                    return;
                }

                await Task.Run(() =>
                {
                    _ue4ssConfigurationService.Apply(
                        loader);

                    _liveLoaderBridgeService.EnsureInstalled(
                        loader);

                    _nativeBridgeInstallerService.EnsureInstalled(
    loader);
                });

                UpdateGameRunningStatus();
                RefreshSettingsPage();
                ApplyResourceOverlayPreference();

                string warning =
                    cleanup.Errors.Count == 0
                        ? string.Empty
                        : $"\n\n{cleanup.Errors.Count} file(s) could not be removed. The diagnostic report will include the session details.";

                ShowLimelightDialog(
                    cleanup.Errors.Count == 0
                        ? "LIVE LOADER REPAIRED"
                        : "REPAIR FINISHED WITH NOTES",
                    $"Limelight cleared {cleanup.DeletedFileCount} staged file(s) and refreshed its bridge.{warning}",
                    cleanup.Errors.Count == 0
                        ? LimelightDialogTone.Success
                        : LimelightDialogTone.Warning,
                    eyebrow: "REPAIR COMPLETE");
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "REPAIR COULD NOT FINISH",
                    "Limelight did not replace any imported mods.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "REPAIR FAILED");
            }
        }

        private async void PurgeAllModsRequested()
        {
            if (string.IsNullOrWhiteSpace(_gameDirectory))
            {
                ShowLimelightDialog(
                    "GAME NOT CONNECTED",
                    "Connect Limelight to Dead as Disco before clearing its mod folder.",
                    LimelightDialogTone.Warning,
                    eyebrow: "PURGE BLOCKED");

                return;
            }

            string gameDirectory =
                _gameDirectory;

            if (_gameProcessService.IsGameRunning(gameDirectory))
            {
                ShowLimelightDialog(
                    "CLOSE THE GAME FIRST",
                    "Dead as Disco must be closed before Limelight can purge its mod folder.",
                    LimelightDialogTone.Warning,
                    eyebrow: "PURGE BLOCKED");

                return;
            }

            LimelightDialogChoice confirmation =
                ShowLimelightDialog(
                    "PURGE EVERY DEPLOYED MOD?",
                    "This empties Dead as Disco's ~mods folder, including files that were added outside Limelight. Your imported library, profiles, and X19 groups will stay in Limelight.",
                    LimelightDialogTone.Question,
                    primaryAction: "PURGE ALL MODS",
                    secondaryAction: "KEEP MY FILES",
                    eyebrow: "DESTRUCTIVE RECOVERY",
                    footerHint: "The game must remain closed until the purge finishes.");

            if (confirmation != LimelightDialogChoice.Primary)
            {
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    // I close Limelight's session record first so no staged
                    // generation remains associated with files being purged.
                    _liveSessionService.RecoverClosedGame(
                        gameDirectory);

                    _modDeploymentService.PurgeAllMods(
                        gameDirectory);
                });

                _settings.ActiveModId =
                    string.Empty;

                _settings.PendingDeploymentModId =
                    string.Empty;

                _settingsService.Save(_settings);

                RefreshLibrarySummary();
                RefreshSettingsPage();
                UpdateGameRunningStatus();

                ShowLimelightDialog(
                    "MOD FOLDER PURGED",
                    "Dead as Disco's ~mods folder is clean. Your imported mods and profiles remain ready inside Limelight.",
                    LimelightDialogTone.Success,
                    eyebrow: "PURGE COMPLETE");
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "MOD FOLDER COULD NOT BE PURGED",
                    "Limelight stopped before changing your imported library.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "PURGE FAILED");
            }
        }

        private async void CreatePrivateTestReportRequested()
        {
            var reportWindow =
                new PrivateTestReportWindow
                {
                    Owner = this
                };

            if (reportWindow.ShowDialog() != true ||
                reportWindow.ReportRequest is null)
            {
                return;
            }

            string? reportPath =
                LimelightFilePickerWindow.PickSaveFile(
                    this,
                    "Save Limelight private test report",
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.DesktopDirectory),
                    $"Limelight-Test-Report-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
                    ".zip",
                    "ZIP ARCHIVES");

            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            try
            {
                string automaticDiagnostics =
                    await CreateSanitizedDiagnosticReportAsync();

                string loaderMode =
                    _selectedLoaderMode switch
                    {
                        LoaderLaunchMode.X19 => "X19 LLoader",
                        LoaderLaunchMode.Disabled => "No Live Loader",
                        _ => "Live Loader"
                    };

                await _privateTestReportService.CreateArchiveAsync(
                    reportPath,
                    reportWindow.ReportRequest,
                    automaticDiagnostics,
                    loaderMode,
                    _gameDirectory,
                    _nexusApiKey);

                ShowLimelightDialog(
                    "TEST REPORT READY",
                    "The private test report is ready to send. Limelight removed saved paths and private account values from its generated text.",
                    LimelightDialogTone.Success,
                    eyebrow: "PRIVATE TESTING");
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "REPORT COULD NOT BE CREATED",
                    "The selected evidence files were left untouched.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "REPORT FAILED");
            }
        }

        private async void ExportDiagnosticsRequested()
        {
            string? reportPath =
                LimelightFilePickerWindow.PickSaveFile(
                    this,
                    "Save Limelight diagnostic report",
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.DesktopDirectory),
                    $"Limelight-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                    ".txt",
                    "TEXT FILES");

            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            try
            {
                string report =
                    await CreateSanitizedDiagnosticReportAsync();

                await File.WriteAllTextAsync(
                    reportPath,
                    report);

                ShowLimelightDialog(
                    "DIAGNOSTIC REPORT EXPORTED",
                    "The report was saved. Personal and installation paths were replaced with private labels.",
                    LimelightDialogTone.Success,
                    eyebrow: "EXPORT COMPLETE");
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "REPORT COULD NOT BE EXPORTED",
                    "Limelight could not save the diagnostic report.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "EXPORT FAILED");
            }
        }

        private async Task<string> CreateSanitizedDiagnosticReportAsync()
        {
            string? gameDirectory =
                _gameDirectory;

            bool isGameRunning =
                !string.IsNullOrWhiteSpace(gameDirectory) &&
                _gameProcessService.IsGameRunning(
                    gameDirectory);

            Ue4ssDetectionResult loader =
                _ue4ssDetectionService.Detect(
                    gameDirectory);

            LiveSessionState session =
                _liveSessionService.Load();

            LiveSessionCleanupResult stagingSnapshot =
                string.IsNullOrWhiteSpace(gameDirectory)
                    ? new LiveSessionCleanupResult()
                    : _liveSessionService.GetStagingSnapshot(
                        gameDirectory);

            return await Task.Run(() =>
                _diagnosticReportService.CreateReport(
                    _settings,
                    session,
                    gameDirectory,
                    isGameRunning,
                    loader,
                    _compatibilityService.Check(
                        gameDirectory),
                    stagingSnapshot));
        }

        private void ShowBrowseNexus_Click(
    object sender,
    MouseButtonEventArgs e)
        {
            ShowBrowseNexusPage();
        }

        private void BrowseNexus_Click(
            object sender,
            RoutedEventArgs e)
        {
            ShowBrowseNexusPage();
        }

        private void DocumentationButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            const string documentationUrl =
                "https://henreh1.github.io/LimelightWiki/";

            try
            {
                // I let Windows open the guide in the user's usual browser.
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = documentationUrl,
                        UseShellExecute = true
                    });
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "DOCUMENTATION UNAVAILABLE",
                    "Limelight could not open the documentation in your browser.",
                    LimelightDialogTone.Warning,
                    details: exception.Message,
                    eyebrow: "HELP LINK");
            }
        }

        private void ShowBrowseNexusPage()
        {
            DashboardPage.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            ProfilesPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            DownloadsPageControl.Visibility =
                Visibility.Collapsed;

            LiveLoadersPageControl.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Visible;

            bool isConnected =
                NexusApiService.IntegrationEnabled &&
                _nexusAccount is not null &&
                !string.IsNullOrWhiteSpace(_nexusApiKey);

            BrowseNexusPageControl.ShowConnection(
                isConnected);

            SetSelectedNavigation(
                showMyMods: false,
                showBrowseNexus: true);

            // The first visit loads Nexus automatically. Later visits keep the
            // existing cards in place until the user asks for a refresh.
            if (isConnected &&
                !_hasLoadedNexusBrowseMods &&
                !_isNexusBrowseLoading)
            {
                _ = LoadNexusModsAsync(
                    BrowseNexusPageControl.SelectedSortKey);
            }
        }

        private async void NexusSearchRequested(
            string query)
        {
            if (!NexusApiService.IntegrationEnabled)
            {
                return;
            }

            _nexusSearchQuery =
                query.Trim();

            if (TryReadNexusModId(
                    _nexusSearchQuery,
                    out long modId))
            {
                await SearchNexusModByIdAsync(
                    modId);

                return;
            }

            ApplyNexusSearch();
        }

        private async void NexusSortChanged(
            string sortKey)
        {
            await LoadNexusModsAsync(
                sortKey);
        }

        private void NexusCategoryChanged(
            string category)
        {
            _nexusCategoryFilter =
                category.Trim();

            ApplyNexusSearch();
        }

        private async void NexusRefreshRequested()
        {
            await LoadNexusModsAsync(
                BrowseNexusPageControl.SelectedSortKey,
                forceRefresh: true);
        }

        private async Task LoadNexusModsAsync(
            string sortKey,
            bool forceRefresh = false)
        {
            if (_isNexusBrowseLoading)
            {
                return;
            }

            if (_nexusAccount is null ||
                string.IsNullOrWhiteSpace(_nexusApiKey))
            {
                BrowseNexusPageControl.ShowConnection(
                    isConnected: false);

                return;
            }

            _isNexusBrowseLoading =
                true;

            BrowseNexusPageControl.ShowLoading(
                isLoading: true);

            try
            {
                IReadOnlyList<NexusModSummary> mods =
                    await _nexusApiService.GetModsAsync(
                        _nexusApiKey,
                        sortKey,
                        forceRefresh);

                _nexusBrowseMods.Clear();
                _nexusBrowseMods.AddRange(mods);

                BrowseNexusPageControl.ShowCategories(
                    _nexusBrowseMods.Select(mod =>
                        mod.CategoryName));

                _hasLoadedNexusBrowseMods =
                    true;

                ApplyNexusSearch();
            }
            catch (UnauthorizedAccessException exception)
            {
                _hasLoadedNexusBrowseMods =
                    false;

                BrowseNexusPageControl.ShowError(
                    exception.Message);
            }
            catch (Exception exception)
            {
                _hasLoadedNexusBrowseMods =
                    false;

                BrowseNexusPageControl.ShowError(
                    "Limelight could not load the Dead as Disco mod library. " +
                    exception.Message);
            }
            finally
            {
                _isNexusBrowseLoading =
                    false;

                BrowseNexusPageControl.ShowLoading(
                    isLoading: false);
            }
        }

        private void ApplyNexusSearch()
        {
            IEnumerable<NexusModSummary> matches =
                _nexusBrowseMods;

            // Category and title filters work together, just like the Nexus
            // catalogue, so a character search stays inside Characters.
            if (!string.IsNullOrWhiteSpace(
                    _nexusCategoryFilter))
            {
                matches =
                    matches.Where(mod =>
                        mod.CategoryName.Equals(
                            _nexusCategoryFilter,
                            StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(
                    _nexusSearchQuery))
            {
                string normalisedQuery =
                    string.Join(
                        " ",
                        _nexusSearchQuery.Split(
                            ' ',
                            StringSplitOptions.RemoveEmptyEntries |
                            StringSplitOptions.TrimEntries));

                string[] searchTerms =
                    normalisedQuery.Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries);

                matches =
                    matches
                        .Select(mod =>
                            new
                            {
                                Mod = mod,
                                Score = GetNexusSearchScore(
                                    mod,
                                    normalisedQuery,
                                    searchTerms)
                            })
                        .Where(result =>
                            result.Score < int.MaxValue)
                        .OrderBy(result => result.Score)
                        .ThenByDescending(result =>
                            result.Mod.Endorsements)
                        .ThenBy(result =>
                            result.Mod.Name)
                        .Select(result => result.Mod);
            }

            BrowseNexusPageControl.ShowMods(
                matches.ToList());
        }

        private static int GetNexusSearchScore(
            NexusModSummary mod,
            string query,
            IReadOnlyCollection<string> searchTerms)
        {
            if (mod.Name.Equals(
                    query,
                    StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (mod.Name.StartsWith(
                    query,
                    StringComparison.OrdinalIgnoreCase))
            {
                return 10;
            }

            if (mod.Name.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase))
            {
                return 20;
            }

            // Matching every word in the title allows searches such as
            // "urara haru" while keeping title results above everything else.
            if (AllSearchTermsMatch(
                    mod.Name,
                    searchTerms))
            {
                return 30;
            }

            int fuzzyTitleDistance =
                GetFuzzyTitleDistance(
                    mod.Name,
                    searchTerms);

            if (fuzzyTitleDistance < int.MaxValue)
            {
                return 40 +
                    fuzzyTitleDistance;
            }

            if (AllSearchTermsMatch(
                    mod.Author,
                    searchTerms))
            {
                return 100;
            }

            if (AllSearchTermsMatch(
                    mod.Summary,
                    searchTerms))
            {
                return 200;
            }

            string combinedDetails =
                $"{mod.Name} {mod.Author} {mod.Summary}";

            return AllSearchTermsMatch(
                    combinedDetails,
                    searchTerms)
                ? 300
                : int.MaxValue;
        }

        private static int GetFuzzyTitleDistance(
            string title,
            IReadOnlyCollection<string> searchTerms)
        {
            string[] titleWords =
                GetSearchWords(title);

            string[] queryWords =
                GetSearchWords(
                    string.Join(
                        " ",
                        searchTerms));

            if (titleWords.Length == 0 ||
                queryWords.Length == 0)
            {
                return int.MaxValue;
            }

            int totalDistance = 0;

            foreach (string queryWord in queryWords)
            {
                int bestDistance =
                    titleWords
                        .Select(titleWord =>
                            GetFuzzyWordDistance(
                                queryWord,
                                titleWord))
                        .DefaultIfEmpty(int.MaxValue)
                        .Min();

                int allowedDistance =
                    queryWord.Length switch
                    {
                        <= 2 => 0,
                        <= 5 => 1,
                        <= 9 => 2,
                        _ => 3
                    };

                if (bestDistance > allowedDistance)
                {
                    return int.MaxValue;
                }

                totalDistance +=
                    bestDistance;
            }

            return totalDistance;
        }

        private static int GetFuzzyWordDistance(
            string queryWord,
            string titleWord)
        {
            // A partial word is useful while someone is still typing, while
            // the distance check below catches missing or swapped letters.
            if (titleWord.Contains(
                    queryWord,
                    StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            return GetDamerauLevenshteinDistance(
                queryWord,
                titleWord);
        }

        private static string[] GetSearchWords(
            string value)
        {
            string lettersAndSpaces =
                new string(
                    value
                        .Select(character =>
                            char.IsLetterOrDigit(character)
                                ? char.ToLowerInvariant(character)
                                : ' ')
                        .ToArray());

            return lettersAndSpaces.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);
        }

        private static int GetDamerauLevenshteinDistance(
            string source,
            string target)
        {
            var distances =
                new int[
                    source.Length + 1,
                    target.Length + 1];

            for (int sourceIndex = 0;
                sourceIndex <= source.Length;
                sourceIndex++)
            {
                distances[sourceIndex, 0] =
                    sourceIndex;
            }

            for (int targetIndex = 0;
                targetIndex <= target.Length;
                targetIndex++)
            {
                distances[0, targetIndex] =
                    targetIndex;
            }

            for (int sourceIndex = 1;
                sourceIndex <= source.Length;
                sourceIndex++)
            {
                for (int targetIndex = 1;
                    targetIndex <= target.Length;
                    targetIndex++)
                {
                    int substitutionCost =
                        source[sourceIndex - 1] ==
                        target[targetIndex - 1]
                            ? 0
                            : 1;

                    distances[sourceIndex, targetIndex] =
                        Math.Min(
                            Math.Min(
                                distances[sourceIndex - 1, targetIndex] + 1,
                                distances[sourceIndex, targetIndex - 1] + 1),
                            distances[sourceIndex - 1, targetIndex - 1] +
                            substitutionCost);

                    if (sourceIndex > 1 &&
                        targetIndex > 1 &&
                        source[sourceIndex - 1] ==
                        target[targetIndex - 2] &&
                        source[sourceIndex - 2] ==
                        target[targetIndex - 1])
                    {
                        distances[sourceIndex, targetIndex] =
                            Math.Min(
                                distances[sourceIndex, targetIndex],
                                distances[sourceIndex - 2, targetIndex - 2] +
                                substitutionCost);
                    }
                }
            }

            return distances[
                source.Length,
                target.Length];
        }

        private static bool AllSearchTermsMatch(
            string value,
            IReadOnlyCollection<string> searchTerms)
        {
            return searchTerms.Count > 0 &&
                searchTerms.All(term =>
                    value.Contains(
                        term,
                        StringComparison.OrdinalIgnoreCase));
        }

        private async Task SearchNexusModByIdAsync(
            long modId)
        {
            if (_isNexusBrowseLoading ||
                _nexusAccount is null ||
                string.IsNullOrWhiteSpace(_nexusApiKey))
            {
                return;
            }

            _isNexusBrowseLoading =
                true;

            BrowseNexusPageControl.ShowLoading(
                isLoading: true);

            try
            {
                NexusModSummary mod =
                    await _nexusApiService.GetModAsync(
                        _nexusApiKey,
                        modId);

                _nexusBrowseMods.RemoveAll(
                    existingMod =>
                        existingMod.ModId == mod.ModId);

                _nexusBrowseMods.Insert(
                    0,
                    mod);

                BrowseNexusPageControl.ShowCategories(
                    _nexusBrowseMods.Select(existingMod =>
                        existingMod.CategoryName));

                _hasLoadedNexusBrowseMods =
                    true;

                BrowseNexusPageControl.ShowMods(
                    new[] { mod });
            }
            catch (UnauthorizedAccessException exception)
            {
                BrowseNexusPageControl.ShowError(
                    exception.Message);
            }
            catch (Exception exception)
            {
                BrowseNexusPageControl.ShowError(
                    "Limelight could not find that Dead as Disco mod. " +
                    exception.Message);
            }
            finally
            {
                _isNexusBrowseLoading =
                    false;

                BrowseNexusPageControl.ShowLoading(
                    isLoading: false);
            }
        }

        private static bool TryReadNexusModId(
            string query,
            out long modId)
        {
            modId = 0;

            string trimmedQuery =
                query.Trim();

            if (long.TryParse(
                    trimmedQuery,
                    out modId) &&
                modId > 0)
            {
                return true;
            }

            if (!Uri.TryCreate(
                    trimmedQuery,
                    UriKind.Absolute,
                    out Uri? uri))
            {
                modId = 0;
                return false;
            }

            bool isNexusHost =
                uri.Host.Equals(
                    "nexusmods.com",
                    StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(
                    ".nexusmods.com",
                    StringComparison.OrdinalIgnoreCase);

            if (!isNexusHost)
            {
                modId = 0;
                return false;
            }

            string[] pathParts =
                uri.AbsolutePath.Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries);

            int modsIndex =
                Array.FindIndex(
                    pathParts,
                    part => part.Equals(
                        "mods",
                        StringComparison.OrdinalIgnoreCase));

            bool isDeadAsDiscoMod =
                modsIndex > 0 &&
                modsIndex + 1 < pathParts.Length &&
                pathParts[modsIndex - 1].Equals(
                    "deadasdisco",
                    StringComparison.OrdinalIgnoreCase);

            if (!isDeadAsDiscoMod ||
                !long.TryParse(
                    pathParts[modsIndex + 1],
                    out modId) ||
                modId <= 0)
            {
                modId = 0;
                return false;
            }

            return true;
        }
        private async void ImportMod_Click(
            object sender,
            RoutedEventArgs e)
        {
            string? archivePath =
                LimelightFilePickerWindow.PickFile(
                    this,
                    "Choose a Dead as Disco mod",
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.UserProfile),
                        "Downloads"),
                    new[] { ".zip" },
                    "ZIP ARCHIVES");

            if (string.IsNullOrWhiteSpace(archivePath))
            {
                return;
            }

            await ImportModArchiveAsync(
                archivePath);
        }

        private void MainWindow_PreviewDragEnter(
            object sender,
            DragEventArgs e)
        {
            UpdateModDropFeedback(e);
        }

        private void MainWindow_PreviewDragOver(
            object sender,
            DragEventArgs e)
        {
            UpdateModDropFeedback(e);
        }

        private void MainWindow_PreviewDragLeave(
            object sender,
            DragEventArgs e)
        {
            Point pointerPosition =
                e.GetPosition(this);

            // Routed drag events can also fire while the pointer moves between
            // child controls. I only hide the cue after it leaves the window.
            if (pointerPosition.X <= 0 ||
                pointerPosition.Y <= 0 ||
                pointerPosition.X >= ActualWidth ||
                pointerPosition.Y >= ActualHeight)
            {
                ModDropOverlay.Visibility =
                    Visibility.Collapsed;
            }

            e.Handled = true;
        }

        private async void MainWindow_PreviewDrop(
            object sender,
            DragEventArgs e)
        {
            ModDropOverlay.Visibility =
                Visibility.Collapsed;

            e.Handled = true;

            string[] archivePaths =
                GetDroppedZipArchives(
                    e.Data);

            if (archivePaths.Length == 0)
            {
                ShowLimelightDialog(
                    "ZIP ARCHIVE REQUIRED",
                    "Drop one or more Dead as Disco mod ZIP archives into Limelight.",
                    LimelightDialogTone.Error,
                    eyebrow: "IMPORT MISSED ITS CUE");

                return;
            }

            // Multiple archives are handled in the order Windows provides
            // them, using exactly the same checks as the Import Mod button.
            foreach (string archivePath in archivePaths)
            {
                await ImportModArchiveAsync(
                    archivePath);
            }
        }

        private void UpdateModDropFeedback(
            DragEventArgs e)
        {
            bool canImport =
                !_isModImportInProgress &&
                GetDroppedZipArchives(
                        e.Data)
                    .Length > 0;

            e.Effects = canImport
                ? DragDropEffects.Copy
                : DragDropEffects.None;

            ModDropOverlay.Visibility = canImport
                ? Visibility.Visible
                : Visibility.Collapsed;

            e.Handled = true;
        }

        private static string[] GetDroppedZipArchives(
            IDataObject data)
        {
            if (!data.GetDataPresent(
                    DataFormats.FileDrop))
            {
                return Array.Empty<string>();
            }

            string[] droppedPaths =
                data.GetData(
                    DataFormats.FileDrop) as string[]
                ?? Array.Empty<string>();

            return droppedPaths
                .Where(path =>
                    File.Exists(path) &&
                    string.Equals(
                        Path.GetExtension(path),
                        ".zip",
                        StringComparison.OrdinalIgnoreCase))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private async Task ImportModArchiveAsync(
            string archivePath)
        {
            if (_isModImportInProgress)
            {
                ShowLimelightDialog(
                    "IMPORT ALREADY IN PROGRESS",
                    "Let Limelight finish adding the current archive before importing another mod.",
                    LimelightDialogTone.Information,
                    eyebrow: "ONE CUE AT A TIME");

                return;
            }

            if (!File.Exists(archivePath) ||
                !string.Equals(
                    Path.GetExtension(archivePath),
                    ".zip",
                    StringComparison.OrdinalIgnoreCase))
            {
                ShowLimelightDialog(
                    "ZIP ARCHIVE REQUIRED",
                    "Limelight can only import mod archives saved as ZIP files.",
                    LimelightDialogTone.Error,
                    eyebrow: "IMPORT MISSED ITS CUE");

                return;
            }

            _isModImportInProgress = true;
            ImportModButton.IsEnabled = false;
            ImportModButton.Content = "IMPORTING...";

            try
            {
                ModArchiveFingerprintResult fingerprintResult =
                    await Task.Run(() =>
                        _modLibraryService.GetArchiveFingerprintResult(
                            archivePath));

                if (!fingerprintResult.IsValid)
                {
                    ShowLimelightDialog(
                        "NOT A MOD ARCHIVE",
                        "Limelight could not find a supported Dead as Disco mod in this ZIP.",
                        LimelightDialogTone.Error,
                        details: fingerprintResult.Message,
                        eyebrow: "IMPORT SKIPPED");

                    return;
                }

                string incomingFingerprint =
                    fingerprintResult.Fingerprint;

                List<(InstalledMod Mod, string Fingerprint)> libraryFingerprints =
                    await Task.Run(
                        CalculateLibraryFingerprints);

                bool fingerprintsAdded = false;

                foreach ((InstalledMod mod, string fingerprint) in
                         libraryFingerprints)
                {
                    if (string.IsNullOrWhiteSpace(
                            mod.ContentFingerprint))
                    {
                        // Older libraries did not store fingerprints. I fill
                        // them in once so renamed legacy mods are protected too.
                        mod.ContentFingerprint = fingerprint;
                        fingerprintsAdded = true;
                    }
                }

                if (fingerprintsAdded)
                {
                    _settingsService.Save(
                        _settings);
                }

                InstalledMod? existingMod =
                    libraryFingerprints
                        .FirstOrDefault(item =>
                            string.Equals(
                                item.Fingerprint,
                                incomingFingerprint,
                                StringComparison.OrdinalIgnoreCase))
                        .Mod;

                if (existingMod != null)
                {
                    ShowLimelightDialog(
                        "MOD ALREADY INSTALLED",
                        $"{existingMod.DisplayName} already contains the same mod files. Renaming a library entry does not create a separate copy.",
                        LimelightDialogTone.Information,
                        primaryAction: "VIEW MY MODS",
                        eyebrow: "IMPORT SKIPPED");

                    return;
                }

                // Large archives are processed in the background so
                // the interface remains responsive during the import.
                InstalledMod installedMod =
                    await Task.Run(() =>
                        _modLibraryService.Import(
                            archivePath,
                            contentFingerprint: incomingFingerprint));

                _settings.InstalledMods.Add(
                    installedMod);

                _settingsService.Save(_settings);

                RefreshLibrarySummary();

                ShowLimelightDialog(
                    "MOD IMPORTED",
                    $"{installedMod.DisplayName} was added to your library.",
                    LimelightDialogTone.Success,
                    details:
                        $"Package files: {installedMod.PackageFiles.Count}\n" +
                        $"Assets detected: {installedMod.AssetPackages.Count}\n" +
                        "Live-refreshable: " +
                        $"{installedMod.AssetPackages.Count(package => package.IsSafeForLiveReload)}",
                    eyebrow: "READY FOR THE SPOTLIGHT");
            }
            catch (Exception exception)
            {
                ShowLimelightDialog(
                    "MOD IMPORT FAILED",
                    "Limelight could not add this archive to the library.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "IMPORT MISSED ITS CUE");
            }
            finally
            {
                _isModImportInProgress = false;
                ImportModButton.IsEnabled = true;
                ImportModButton.Content = "IMPORT MOD";
            }
        }

        private List<(InstalledMod Mod, string Fingerprint)>
            CalculateLibraryFingerprints()
        {
            List<(InstalledMod Mod, string Fingerprint)> fingerprints =
                new List<(InstalledMod Mod, string Fingerprint)>();

            foreach (InstalledMod mod in _settings.InstalledMods)
            {
                if (!Directory.Exists(
                        mod.InstallDirectory))
                {
                    continue;
                }

                try
                {
                    string fingerprint =
                        _modLibraryService
                            .CalculateInstalledModFingerprint(mod);

                    fingerprints.Add(
                        (mod, fingerprint));
                }
                catch (IOException)
                {
                    // A damaged legacy entry should not block imports for the
                    // rest of the library. Its normal validation still reports it.
                }
                catch (UnauthorizedAccessException)
                {
                    // Security software can briefly hold an old package file.
                    // I skip only that entry and leave the rest of the scan intact.
                }
            }

            return fingerprints;
        }

        private void RefreshLibrarySummary()
        {
            // Ignore entries whose extracted folder was manually removed.
            List<InstalledMod> availableMods =
                _settings.InstalledMods
                    .Where(mod =>
                        Directory.Exists(
                            mod.InstallDirectory))
                    .ToList();

            InstalledMod? activeMod =
                availableMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        _settings.ActiveModId,
                        StringComparison.OrdinalIgnoreCase));

            // A missing library folder means the saved active selection
            // is no longer valid.
            if (activeMod == null &&
                !string.IsNullOrWhiteSpace(
                    _settings.ActiveModId))
            {
                _settings.ActiveModId =
                    string.Empty;

                _settingsService.Save(_settings);
            }

            int installedCount =
                availableMods.Count;

            MyModsPageControl.ShowMods(
                availableMods,
                _settings.ActiveModId);

            ProfilesPageControl.ShowProfiles(
                _settings.ModProfiles,
                availableMods);

            LiveLoadersPageControl.ShowConfiguration(
                availableMods,
                _settings.X19LoaderModIds,
                _settings.X19LoaderProfileIds,
                _settings.ActiveModId,
                _settings.X19HotkeyGesture,
                _settings.X19ShuffleEnabled,
                _settings.ModProfiles);

            InstalledModCountText.Text =
                installedCount.ToString();

            ActiveModelText.Text =
                activeMod?.DisplayName.ToUpperInvariant()
                ?? "NONE";

            InstalledModCountText.Foreground =
    (Brush)FindResource(
        installedCount == 0
            ? "PinkBrush"
            : "CyanBrush");

            ActiveModelText.Foreground =
                (Brush)FindResource(
                    activeMod is null
                        ? "PinkBrush"
                        : "CyanBrush");

            UpdateSpotlightBanner(activeMod);
            RefreshDiscordPresence();

            if (installedCount == 0)
            {
                LibrarySummaryText.Text =
                    "Your mod library is empty. Import or drag in a ZIP archive to get started.";

                LibraryStatusText.Text =
                    "NO MODS YET";
                LibraryStatusText.Foreground =
    (Brush)FindResource("PinkBrush");

                return;
            }

            LibrarySummaryText.Text =
                installedCount == 1
                    ? "1 mod is installed and ready to activate."
                    : $"{installedCount} mods are installed and ready to activate.";

            LibraryStatusText.Text =
                $"{installedCount} READY";
        }

        private void UpdateSpotlightBanner(
            InstalledMod? activeMod)
        {
            if (string.IsNullOrWhiteSpace(_gameDirectory))
            {
                SpotlightTitleText.Text =
                    "READY FOR THE SPOTLIGHT?";

                SpotlightDescriptionText.Text =
                    "Connect your game directory, install a character mod, and take control of the stage.";

                ConnectGameButton.Content =
                    "CONNECT GAME";

                return;
            }

            if (activeMod is null)
            {
                SpotlightTitleText.Text =
                    "CHOOSE YOUR HEADLINER";

                SpotlightDescriptionText.Text =
                    "Dead as Disco is connected. Choose a character model to take the spotlight.";

                ConnectGameButton.Content =
                    "CHOOSE MODEL";

                return;
            }

            SpotlightTitleText.Text =
                $"{activeMod.DisplayName.ToUpperInvariant()} HAS THE SPOTLIGHT";

            SpotlightDescriptionText.Text =
                "Your selected character is installed and ready for the next performance.";

            ConnectGameButton.Content =
                "SWITCH MODEL";
        }

        private static ProcessStartInfo CreateSteamLaunchStartInfo()
        {
            const string steamAppId =
                "3404260";

            string? steamExecutable =
                FindSteamExecutable();

            if (!string.IsNullOrWhiteSpace(steamExecutable))
            {
                return new ProcessStartInfo
                {
                    // Steam's explicit app-launch command is more dependable
                    // than asking Windows to forward a steam:// link.
                    FileName = steamExecutable,
                    Arguments = $"-applaunch {steamAppId}",
                    WorkingDirectory =
                        Path.GetDirectoryName(steamExecutable) ??
                        string.Empty,
                    UseShellExecute = false
                };
            }

            // Keep the registered protocol as a fallback for unusual Steam
            // installs whose executable path is not available in the registry.
            return new ProcessStartInfo
            {
                FileName =
                    $"steam://rungameid/{steamAppId}",
                UseShellExecute = true
            };
        }

        private static string? FindSteamExecutable()
        {
            string? currentUserSteam =
                Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Valve\Steam",
                    "SteamExe",
                    null) as string;

            string? localMachineSteam =
                Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                    "InstallPath",
                    null) as string;

            string? localMachineSteam64 =
                Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam",
                    "InstallPath",
                    null) as string;

            string? programFilesX86 =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86);

            string?[] candidates =
            {
                currentUserSteam,
                string.IsNullOrWhiteSpace(localMachineSteam)
                    ? null
                    : Path.Combine(localMachineSteam, "steam.exe"),
                string.IsNullOrWhiteSpace(localMachineSteam64)
                    ? null
                    : Path.Combine(localMachineSteam64, "steam.exe"),
                string.IsNullOrWhiteSpace(programFilesX86)
                    ? null
                    : Path.Combine(programFilesX86, "Steam", "steam.exe")
            };

            foreach (string? candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                string normalizedCandidate =
                    candidate.Replace('/', Path.DirectorySeparatorChar);

                if (File.Exists(normalizedCandidate))
                {
                    return normalizedCandidate;
                }
            }

            return null;
        }

        private static void WriteLaunchTrace(
            string message)
        {
            try
            {
                string logDirectory =
                    Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "Limelight",
                        "Logs");

                Directory.CreateDirectory(logDirectory);

                string logPath =
                    Path.Combine(
                        logDirectory,
                        "launch.log");

                // I keep this trace deliberately small. It only records launch
                // stages, but gives us a useful answer if Steam ever stays quiet.
                if (File.Exists(logPath) &&
                    new FileInfo(logPath).Length > 512 * 1024)
                {
                    File.WriteAllText(
                        logPath,
                        string.Empty);
                }

                File.AppendAllText(
                    logPath,
                    $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
            }
            catch
            {
                // A diagnostic trace must never be allowed to block a launch.
            }
        }

        private async void LaunchGame_Click(
    object sender,
    RoutedEventArgs e)
        {
            WriteLaunchTrace(
                "Launch button selected.");

            string? gameDirectory =
                _gameDirectory;

            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                ShowLimelightDialog(
                    "GAME NOT CONNECTED",
                    "Connect Limelight to your Dead as Disco folder before launching the game.",
                    LimelightDialogTone.Warning,
                    eyebrow: "LAUNCH BLOCKED");

                return;
            }

            if (_gameProcessService.IsGameRunning(gameDirectory))
            {
                // Starting a second copy can cause Steam or the game to display
                // confusing errors, so keep the already-running instance.
                ShowLimelightDialog(
                    "GAME ALREADY RUNNING",
                    "Limelight found the existing Dead as Disco session and will not start a second copy.",
                    LimelightDialogTone.Information,
                    eyebrow: "LAUNCH SKIPPED");

                return;
            }

            string executablePath =
                Path.Combine(
                    gameDirectory,
                    "Pagoda.exe");

            if (!File.Exists(executablePath))
            {
                ShowLimelightDialog(
                    "GAME EXECUTABLE MISSING",
                    "Limelight could not find Pagoda.exe. Reconnect the game folder in Settings and try again.",
                    LimelightDialogTone.Warning,
                    eyebrow: "LAUNCH BLOCKED");

                return;
            }

            List<InstalledMod> x19Rotation =
                GetX19Rotation();

            LocalCompatibilityResult compatibility =
                _compatibilityService.Check(
                    gameDirectory);

            LoaderModeSelectionWindow modeWindow =
                new LoaderModeSelectionWindow(
                    x19Rotation.Count,
                    _settings.X19HotkeyGesture,
                    compatibility)
                {
                    Owner = this
                };

            bool? modeAccepted =
                modeWindow.ShowDialog();

            WriteLaunchTrace(
                "Loader selector closed: " +
                $"accepted={modeAccepted}; " +
                $"mode={modeWindow.SelectedMode?.ToString() ?? "NONE"}; " +
                $"configureX19={modeWindow.ConfigureX19Requested}; " +
                $"openSupport={modeWindow.OpenSupportRequested}.");

            if (modeAccepted != true ||
                modeWindow.SelectedMode is null)
            {
                if (modeWindow.ConfigureX19Requested)
                {
                    ShowLiveLoadersPage();
                }

                if (modeWindow.OpenSupportRequested)
                {
                    ShowSettingsPage();
                    SettingsPageControl.ShowSupportCategory();

                    ShowNotification(
                        "LIVE LOADER NEEDS ATTENTION",
                        compatibility.Detail,
                        isError: true);
                }

                return;
            }

            _selectedLoaderMode =
                modeWindow.SelectedMode.Value;

            WriteLaunchTrace(
                $"Launch mode accepted: {_selectedLoaderMode}.");

            _globalHotkeyService.Unregister();

            try
            {
                _liveLoaderBridgeService.SetSessionBypass(
                    _selectedLoaderMode ==
                        LoaderLaunchMode.Disabled);

                if (_selectedLoaderMode !=
                    LoaderLaunchMode.Disabled)
                {
                    WriteLaunchTrace(
                        "Checking Live Loader readiness.");

                    // Recheck immediately before touching the game directory.
                    // Steam may have finished an update while the selector was open.
                    compatibility =
                        _compatibilityService.Check(
                            gameDirectory);

                    if (!compatibility.IsLiveLoaderCompatible)
                    {
                        throw new InvalidOperationException(
                            compatibility.Detail);
                    }

                    Ue4ssDetectionResult loader =
                        _ue4ssDetectionService.Detect(
                            gameDirectory);

                    if (!loader.IsInstalled ||
                        !_ue4ssConfigurationService.IsRuntimeCompatible(loader) ||
                        !_ue4ssConfigurationService.IsConfigured(loader) ||
                        !_liveLoaderBridgeService.IsInstalled(loader) ||
                        !_nativeBridgeInstallerService.IsCurrentVersionInstalled(loader))
                    {
                        throw new InvalidOperationException(
                            "The Live Loader needs to be repaired before this launch. " +
                            "Open Settings, choose Support, then select Repair Live Loader.");
                    }

                    // Installation and repair belong to the setup and Support
                    // flows. The launch button only verifies those files so a
                    // locked game folder cannot hold Steam's request hostage.
                    WriteLaunchTrace(
                        "Live Loader readiness check passed.");
                }

                ProcessStartInfo startInfo =
                    CreateSteamLaunchStartInfo();

                WriteLaunchTrace(
                    $"Sending Steam launch request with {startInfo.FileName} {startInfo.Arguments}".TrimEnd());

                if (_selectedLoaderMode !=
                    LoaderLaunchMode.Disabled)
                {
                    // A fresh game launch must produce a fresh heartbeat before the dashboard
                    // is allowed to report the bridge as online.
                    _liveLoaderBridgeService.ClearHeartbeat();
                }

                // Ask Steam to launch its registered Dead as Disco installation.
                using Process? steamLaunch =
                    Process.Start(startInfo);

                if (steamLaunch is null)
                {
                    throw new InvalidOperationException(
                        "Windows did not accept Limelight's Steam launch request.");
                }

                WriteLaunchTrace(
                    "Steam accepted the launch request.");

                if (_selectedLoaderMode !=
                    LoaderLaunchMode.Disabled)
                {
                    // Keep Limelight locked while the runtime comes online and the
                    // active mod is mounted. This removes the tempting-but-unsafe
                    // window where a user can switch mods during LoadMap.
                    await InitialiseLiveLoaderForRunningGameAsync(
                        waitForGameProcess: true);

                    // The process timer may notice the game a fraction earlier than
                    // this launch path. I wait for that shared setup to finish before
                    // deciding whether X19 can register its hotkey.
                    DateTime initialisationDeadline =
                        DateTime.UtcNow.AddMinutes(6);

                    while (_isLiveLoaderInitializationRunning &&
                           DateTime.UtcNow < initialisationDeadline)
                    {
                        await Task.Delay(100);
                    }
                }

                if (_selectedLoaderMode ==
                    LoaderLaunchMode.X19)
                {
                    if (_liveLoaderBridgeService.IsOnline() &&
                        _hasInitialisedCurrentGameSession &&
                        !_isLiveLoaderInitializationRunning)
                    {
                        EnableX19Hotkey();
                    }
                    else
                    {
                        _selectedLoaderMode =
                            LoaderLaunchMode.Normal;

                        ShowNotification(
                            "X19 COULD NOT START",
                            "The Live Loader did not come online, so the X19 hotkey is unavailable for this session.",
                            isError: true);
                    }
                }
            }
            catch (Exception exception)
            {
                WriteLaunchTrace(
                    $"Launch failed: {exception.GetType().Name}: {exception.Message}");

                _globalHotkeyService.Unregister();
                _liveLoaderBridgeService.SetSessionBypass(
                    isDisabled: false);

                _selectedLoaderMode =
                    LoaderLaunchMode.Normal;

                ShowLimelightDialog(
                    "DEAD AS DISCO COULD NOT START",
                    "Limelight restored its launch state and left the game files unchanged.",
                    LimelightDialogTone.Error,
                    details: exception.Message,
                    eyebrow: "LAUNCH FAILED");
            }
        }
        private async void ConnectGame_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_gameDirectory))
            {
                ShowMyModsPage();
                return;
            }

            await ChooseGameDirectoryAsync();
        }

        private async void ChangeGameFolderRequested()
        {
            await ChooseGameDirectoryAsync();
        }

        private async Task ChooseGameDirectoryAsync()
        {
            // Ask for the main installation folder instead of making
            // the user locate the internal Paks directory.
            string? selectedDirectory =
                LimelightFilePickerWindow.PickFolder(
                    this,
                    "Choose the Dead as Disco installation folder",
                    _gameDirectory);

            // Cancelling leaves the current connection unchanged.
            if (string.IsNullOrWhiteSpace(selectedDirectory))
            {
                return;
            }

            if (!TryConnectToGame(
                    selectedDirectory,
                    showError: true))
            {
                return;
            }

            // Store the directory only after it passes validation.
            _settings.GameDirectory =
                selectedDirectory;

            _settingsService.Save(_settings);

            ShowLimelightDialog(
                "GAME CONNECTED",
                "Dead as Disco was connected successfully.",
                LimelightDialogTone.Success,
                eyebrow: "DIRECTORY READY");

            // A newly selected folder should receive its own optional-loader prompt.
            _hasHandledLiveLoaderPrompt = false;

            await CheckForExistingMods();
            await ShowLiveLoaderSetupPromptIfNeeded();
        }

        private bool TryConnectToGame(
            string selectedDirectory,
            bool showError)
        {
            string gameExecutable = Path.Combine(
                selectedDirectory,
                "Pagoda.exe");

            string pakDirectory = Path.Combine(
                selectedDirectory,
                "Pagoda",
                "Content",
                "Paks");

            // Both paths are checked so an unrelated folder containing
            // a file named Pagoda.exe is not accepted accidentally.
            bool validDirectory =
                File.Exists(gameExecutable) &&
                Directory.Exists(pakDirectory);

            if (!validDirectory)
            {
                if (showError)
                {
                    ShowLimelightDialog(
                        "THAT IS NOT THE GAME FOLDER",
                        "Limelight could not find Pagoda.exe and the game's Paks folder. Select the main Dead as Disco folder, not the Paks folder itself.",
                        LimelightDialogTone.Warning,
                        eyebrow: "INVALID DIRECTORY");
                }

                return false;
            }

            _gameDirectory =
                selectedDirectory;

            // Give the user a clear indication that validation passed.
            GameStatusDot.Fill =
                (Brush)FindResource("CyanBrush");

            GameStatusTitle.Text =
                "GAME CONNECTED";

            GameStatusDescription.Text =
                selectedDirectory;

            RefreshSettingsPage();
            RefreshLibrarySummary();

            return true;
        }

        private void RestoreSavedGameDirectory()
        {
            if (string.IsNullOrWhiteSpace(
                    _settings.GameDirectory))
            {
                return;
            }

            // Steam library moves and game updates can invalidate a
            // previously saved location, so check it on every launch.
            if (TryConnectToGame(
                    _settings.GameDirectory,
                    showError: false))
            {
                return;
            }

            _settings.GameDirectory =
                string.Empty;

            _settingsService.Save(_settings);

            GameStatusDescription.Text =
                "The previously selected game folder could not be found.";
        }
    }
}
