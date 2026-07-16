using Limelight.Models;
using Limelight.Services;
using Limelight.Views;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            BrowseNexus,
            Settings
        }

        private readonly SettingsService _settingsService;
        private readonly ModLibraryService _modLibraryService;
        private readonly AppSettings _settings;
        private readonly ModDeploymentService _modDeploymentService;
        private readonly ExistingModsMigrationService _existingModsMigrationService;
        private readonly GameProcessService _gameProcessService;
        private readonly Ue4ssDetectionService _ue4ssDetectionService;
        private readonly Ue4ssReleaseService _ue4ssReleaseService;
        private readonly Ue4ssInstallerService _ue4ssInstallerService;
        private readonly DeadAsDiscoUe4ssConfigurationService _ue4ssConfigurationService;
        private readonly LiveLoaderBridgeService _liveLoaderBridgeService;
        private readonly LiveLoaderCommandService _liveLoaderCommandService;
        private readonly LiveModStagingService _liveModStagingService;
        private readonly LiveSessionService _liveSessionService;
        private readonly DiagnosticReportService _diagnosticReportService;
        private readonly NexusApiService _nexusApiService;
        private readonly NexusCredentialService _nexusCredentialService;

        private NexusAccount? _nexusAccount;

        private string _nexusApiKey =
            string.Empty;
        private readonly List<NexusModSummary> _nexusBrowseMods =
    new();

        private string _nexusSearchQuery =
            string.Empty;

        private string _nexusCategoryFilter =
            string.Empty;

        private bool _isNexusBrowseLoading;
        private bool _hasLoadedNexusBrowseMods;
        private readonly DispatcherTimer _gameStatusTimer;
        private bool _hasHandledLiveLoaderPrompt;
        private bool _isLiveLoaderSetupRunning;
        private bool _isLiveModChangeRunning;
        private bool _isLiveLoaderInitializationRunning;
        private bool _hasInitialisedCurrentGameSession;
        private bool _wasGameRunning;
        private bool _isApplyingPendingDeployment;
        private bool _pendingDeploymentAttempted;
        private int _nextLiveMountOrder = 1000;
        private int _notificationSequence;
        private NavigationPage _selectedNavigationPage =
            NavigationPage.Dashboard;

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

            _liveLoaderCommandService =
                new LiveLoaderCommandService();

            _liveModStagingService =
                new LiveModStagingService();

            _liveSessionService =
                new LiveSessionService();

            _diagnosticReportService =
                new DiagnosticReportService();

            _nexusApiService =
                new NexusApiService();

            _nexusCredentialService =
                new NexusCredentialService();

            _settings =
                _settingsService.Load();

            // The page reports its button clicks to the main window, where
            // the settings and connected game directory are available.
            MyModsPageControl.ToggleModRequested +=
                ToggleModRequested;

            MyModsPageControl.RemoveModRequested +=
                RemoveModRequested;

            SettingsPageControl.RepairRequested +=
                RepairLiveLoaderRequested;

            SettingsPageControl.ExportDiagnosticsRequested +=
                ExportDiagnosticsRequested;

            SettingsPageControl.ChangeGameFolderRequested +=
                ChangeGameFolderRequested;

            SettingsPageControl.NexusConnectRequested +=
                NexusConnectRequested;

            SettingsPageControl.NexusDisconnectRequested +=
                NexusDisconnectRequested;

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

            // Wait until the window is visible before starting timers or
            // showing the existing-mod migration prompt.
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
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

                // A previous crash can leave staged containers behind. They
                // are safe to remove once Windows confirms the game is closed.
                await Task.Run(() =>
                    _liveSessionService.RecoverClosedGame(
                        gameDirectory));
            }

            RefreshSettingsPage();
            await RestoreNexusConnectionAsync();

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
                _liveSessionService.EnsureSession(
                    _gameDirectory!);

                await InitialiseLiveLoaderForRunningGameAsync(
                    waitForGameProcess: false);
            }

            RefreshSettingsPage();
        }

        private void MainWindow_Closed(
            object? sender,
            EventArgs e)
        {
            // The timer belongs to this window, so there is no reason to leave
            // it checking processes after Limelight has closed.
            _gameStatusTimer.Stop();
        }

        private async Task InitialiseLiveLoaderForRunningGameAsync(
            bool waitForGameProcess)
        {
            if (_isLiveLoaderInitializationRunning ||
                _hasInitialisedCurrentGameSession ||
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
                !_liveLoaderBridgeService.IsInstalled(loader))
            {
                // The optional loader has not been accepted yet. The normal
                // dashboard and setup prompt remain available.
                return;
            }

            _isLiveLoaderInitializationRunning = true;

            LiveLoaderInitializingWindow initialisingWindow =
                new LiveLoaderInitializingWindow
                {
                    Owner = this
                };

            bool previousEnabledState =
                IsEnabled;

            Exception? initialisationFailure =
                null;

            try
            {
                initialisingWindow.Show();
                IsEnabled = false;

                initialisingWindow.Report(
                    "WAITING FOR DEAD AS DISCO",
                    8,
                    "Limelight is waiting for the game process to start.");

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

                initialisingWindow.Report(
                    "CONNECTING TO UE4SS",
                    18,
                    "The game is running. Waiting for the Limelight runtime bridge and Unreal object system.");

                DateTime bridgeDeadline =
                    DateTime.UtcNow.AddMinutes(2);

                while (!_liveLoaderBridgeService.IsOnline())
                {
                    if (!_gameProcessService.IsGameRunning(
                            gameDirectory))
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

                InstalledMod? activeMod =
                    _settings.InstalledMods.FirstOrDefault(mod =>
                        string.Equals(
                            mod.Id,
                            _settings.ActiveModId,
                            StringComparison.OrdinalIgnoreCase) &&
                        Directory.Exists(
                            mod.InstallDirectory));

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
                MessageBox.Show(
                    "The Live Loader could not finish initialising.\n\n" +
                    initialisationFailure.Message +
                    "\n\nDead as Disco can still be played normally, but live switching will remain locked for this launch.",
                    "Live Loader initialisation failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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
                MessageBox.Show(
                    "Close Dead as Disco before setting up the live loader.\n\n" +
                    "Limelight will ask again the next time it starts.",
                    "Game is running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

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
                MessageBox.Show(
                    "Limelight could not set up the live loader.\n\n" +
                    setupFailure.Message +
                    "\n\nNo mod-library features were disabled.",
                    "Live-loader setup failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            string backupMessage =
                installResult?.CreatedBackup == true
                    ? "\n\nExisting loader files were backed up before installation."
                    : string.Empty;

            MessageBox.Show(
                "The live loader was set up successfully." +
                backupMessage +
                "\n\nIt will start the next time Dead as Disco launches.",
                "Live loader ready",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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

            MessageBoxResult choice =
                MessageBox.Show(
                    $"Limelight found {modLabel} inside the game's ~mods folder.\n\n" +
                    "Would you like to move them into the Limelight library?\n\n" +
                    "No files will be removed until the library has been saved.",
                    "Existing mods found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (choice != MessageBoxResult.Yes)
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

                MessageBox.Show(
                    "The existing mods were moved into Limelight successfully.\n\n" +
                    "Choose the model you want and select Activate.",
                    "Migration complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"Limelight could not finish the migration.\n\n{exception.Message}",
                    "Migration failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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

            if (!_liveSessionService.CanStageContainers(
                    gameDirectory,
                    upcomingContainerCount,
                    out string limitMessage))
            {
                throw new InvalidOperationException(
                    limitMessage);
            }

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

                _liveSessionService.RecordStagedContainers(
                    mod,
                    stageResult.PakPaths,
                    gameDirectory);

                reportProgress?.Invoke(
                    "MOUNTING MOD CONTENT",
                    60);

                foreach (string pakPath in
                         stageResult.PakPaths)
                {
                    int mountOrder =
                        _nextLiveMountOrder++;

                    LiveLoaderCommandResult mountResult =
                        await _liveLoaderCommandService.MountPakAsync(
                            pakPath,
                            mountOrder);

                    if (!mountResult.Success)
                    {
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

                LiveLoaderCommandResult reloadResult =
                    await _liveLoaderCommandService.ReloadAssetsAsync(
                        livePackages.Select(package =>
                            package.ObjectPath));

                if (!reloadResult.Success)
                {
                    throw new InvalidOperationException(
                        reloadResult.Message);
                }

                LiveLoaderCommandResult reapplyResult =
                    await _liveLoaderCommandService.ReapplyCharlieAsync();

                if (!reapplyResult.Success &&
                    !allowDeferredCharlieRefresh)
                {
                    throw new InvalidOperationException(
                        reapplyResult.Message);
                }

                reportProgress?.Invoke(
                    allowDeferredCharlieRefresh &&
                    !reapplyResult.Success
                        ? "READY: CHARLIE WILL REFRESH WHEN SHE APPEARS"
                        : "LIVE LOADER READY",
                    100);

                _liveSessionService.CompleteActivation(
                    mod);
            }
            catch (Exception exception)
            {
                _liveSessionService.FailActivation(
                    exception);

                throw;
            }
        }

        private async void ToggleModRequested(string modId)
        {
            if (_isLiveModChangeRunning)
            {
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
                return;
            }

            if (string.IsNullOrWhiteSpace(_gameDirectory))
            {
                ShowNotification(
                    "GAME NOT CONNECTED",
                    "Connect the Dead as Disco installation before activating a mod.",
                    isError: true);

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

            if (isCurrentlyActive &&
                isGameRunning)
            {
                ShowNotification(
                    "CLOSE THE GAME TO DEACTIVATE",
                    "The active live container cannot be removed safely while Dead as Disco is running.",
                    isError: true);

                return;
            }

            _isLiveModChangeRunning = true;

            LiveLoaderStatusText.Text =
                isGameRunning
                    ? "SWITCHING"
                    : LiveLoaderStatusText.Text;

            if (isGameRunning)
            {
                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("CyanBrush");
            }

            LiveModSwitchingWindow? switchingWindow =
                null;

            bool previousEnabledState =
                IsEnabled;

            void CloseSwitchingWindow()
            {
                if (switchingWindow is not null)
                {
                    switchingWindow.CloseWhenFinished();
                    switchingWindow = null;
                }

                IsEnabled = previousEnabledState;
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
                    if (!_liveLoaderBridgeService.IsOnline())
                    {
                        throw new InvalidOperationException(
                            "The game is running, but Limelight's Live Loader is not online.");
                    }

                    LiveLoaderCommandResult safetyCheck =
                        await _liveLoaderCommandService.CanSwitchModsAsync();

                    if (!safetyCheck.Success)
                    {
                        // A model change during world teardown can leave the
                        // game holding assets that Unreal has already removed.
                        // Ask the user to wait instead of risking their session.
                        ShowNotification(
                            "LEVEL CHANGE IN PROGRESS",
                            safetyCheck.Message +
                            " Wait until the new level is visible, then select Activate again.",
                            isError: true);

                        return;
                    }

                    bool isFirstLiveSwitch =
                        _nextLiveMountOrder == 1000;

                    switchingWindow =
                        new LiveModSwitchingWindow(
                            selectedMod.DisplayName,
                            isFirstLiveSwitch)
                        {
                            Owner = this
                        };

                    switchingWindow.Show();
                    IsEnabled = false;

                    await ActivateLiveModAsync(
                        selectedMod,
                        gameDirectory,
                        (phase, progress) =>
                            switchingWindow?.Report(
                                phase,
                                progress));

                    _settings.ActiveModId =
                        selectedMod.Id;

                    // The live copy is already active. Once the game closes,
                    // Limelight mirrors the same choice into ~mods for next time.
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

                _settingsService.Save(_settings);
                RefreshLibrarySummary();

                CloseSwitchingWindow();

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

                ShowNotification(
                    notificationTitle,
                    notificationMessage,
                    isError: false);
            }
            catch (Exception exception)
            {
                CloseSwitchingWindow();

                ShowNotification(
                    "MOD ACTIVATION FAILED",
                    exception.Message,
                    isError: true);
            }
            finally
            {
                CloseSwitchingWindow();
                _isLiveModChangeRunning = false;
                UpdateGameRunningStatus();
            }
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

            MessageBoxResult confirmation =
                MessageBox.Show(
                    $"Remove {selectedMod.DisplayName} from Limelight?\n\n" +
                    "This deletes Limelight's stored copy of the mod.",
                    "Remove mod",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
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
                MessageBox.Show(
                    "Close Dead as Disco before removing the active mod from Limelight.",
                    "Mod is active in the running game",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

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
                MessageBox.Show(
                    $"Limelight could not remove this mod.\n\n{exception.Message}",
                    "Remove failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
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

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Visible;

            SetSelectedNavigation(showMyMods: true);
        }

        private async void TestLiveLoader_Click(
    object sender,
    RoutedEventArgs e)
        {
            if (!_liveLoaderBridgeService.IsOnline())
            {
                MessageBox.Show(
                    "Start Dead as Disco and wait for the Live Loader status to show ONLINE.",
                    "Live Loader is offline",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

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

                MessageBox.Show(
                    result.Message,
                    result.Success
                        ? "Native bridge online"
                        : "Native bridge unavailable",
                    MessageBoxButton.OK,
                    result.Success
                        ? MessageBoxImage.Information
                        : MessageBoxImage.Warning);
            }
            catch (Exception exception)
            {
                LiveLoaderStatusText.Text =
                    "TEST FAILED";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("PinkBrush");

                MessageBox.Show(
                    "Limelight could not contact its native bridge.\n\n" +
                    exception.Message,
                    "Native bridge test failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowDashboard_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Collapsed;

            DashboardPage.Visibility =
                Visibility.Visible;

            SetSelectedNavigation(showMyMods: false);
        }

        private void ShowSettings_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            DashboardPage.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Visible;

            RefreshSettingsPage();
            SetSelectedNavigation(
                showMyMods: false,
                showSettings: true);
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
                BrowseNexusNavigation,
                BrowseNexusNavigationIcon,
                BrowseNexusNavigationText,
                _selectedNavigationPage == NavigationPage.BrowseNexus);

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

            if (navigation == BrowseNexusNavigation)
            {
                icon = BrowseNexusNavigationIcon;
                label = BrowseNexusNavigationText;
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
            await ConnectNexusAsync(
                apiKey,
                isRestoring: false);
        }

        private void NexusDisconnectRequested()
        {
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

        private void NexusDownloadRequested(
            NexusModFile file)
        {
            // The picker now hands one exact Nexus file to the download stage.
            // The authenticated download and install queue is the next connection.
            ShowNotification(
                "DOWNLOAD READY",
                $"{file.FileName} is selected and ready for the download stage.",
                isError: false);
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
        }

        private async void RepairLiveLoaderRequested()
        {
            if (string.IsNullOrWhiteSpace(_gameDirectory))
            {
                MessageBox.Show(
                    "Connect Limelight to Dead as Disco before repairing the Live Loader.",
                    "Game not connected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            string gameDirectory =
                _gameDirectory;

            if (_gameProcessService.IsGameRunning(gameDirectory))
            {
                MessageBox.Show(
                    "Close Dead as Disco before repairing the Live Loader.",
                    "Game is running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            MessageBoxResult confirmation =
                MessageBox.Show(
                    "Repair Limelight's managed Live Loader files?\n\n" +
                    "This clears stale live staging files, refreshes the Dead as Disco configuration, and reinstalls Limelight's bridge. Your imported mods are not removed.",
                    "Repair Live Loader",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
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
                });

                UpdateGameRunningStatus();
                RefreshSettingsPage();

                string warning =
                    cleanup.Errors.Count == 0
                        ? string.Empty
                        : $"\n\n{cleanup.Errors.Count} file(s) could not be removed. The diagnostic report will include the session details.";

                MessageBox.Show(
                    "The Live Loader repair is complete.\n\n" +
                    $"Limelight cleared {cleanup.DeletedFileCount} staged file(s) and refreshed its bridge.{warning}",
                    "Repair complete",
                    MessageBoxButton.OK,
                    cleanup.Errors.Count == 0
                        ? MessageBoxImage.Information
                        : MessageBoxImage.Warning);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "Limelight could not complete the Live Loader repair.\n\n" +
                    exception.Message,
                    "Repair failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void ExportDiagnosticsRequested()
        {
            var fileDialog =
                new SaveFileDialog
                {
                    Title = "Save Limelight diagnostic report",
                    Filter = "Text files (*.txt)|*.txt",
                    FileName =
                        $"Limelight-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                    AddExtension = true,
                    DefaultExt = ".txt"
                };

            if (fileDialog.ShowDialog() != true)
            {
                return;
            }

            try
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

                string report =
                    await Task.Run(() =>
                        _diagnosticReportService.CreateReport(
                            _settings,
                            session,
                            gameDirectory,
                            isGameRunning,
                            loader,
                            stagingSnapshot));

                await File.WriteAllTextAsync(
                    fileDialog.FileName,
                    report);

                MessageBox.Show(
                    "The diagnostic report was saved. Personal and installation paths were replaced with private labels.",
                    "Report exported",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "Limelight could not export the diagnostic report.\n\n" +
                    exception.Message,
                    "Export failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
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

        private void ShowBrowseNexusPage()
        {
            DashboardPage.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            SettingsPageControl.Visibility =
                Visibility.Collapsed;

            BrowseNexusPageControl.Visibility =
                Visibility.Visible;

            bool isConnected =
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
            var fileDialog = new OpenFileDialog
            {
                Title = "Choose a Dead as Disco mod",
                Filter = "ZIP archives (*.zip)|*.zip",
                Multiselect = false
            };

            if (fileDialog.ShowDialog() != true)
            {
                return;
            }

            string archiveName =
                Path.GetFileNameWithoutExtension(
                    fileDialog.FileName);

            string incomingModName =
                InstalledMod.CreateDisplayName(
                    archiveName);

            // Compare the cleaned names because Nexus may give the same
            // download a different timestamp or token each time.
            InstalledMod? existingMod =
                _settings.InstalledMods.FirstOrDefault(mod =>
                    Directory.Exists(mod.InstallDirectory) &&
                    string.Equals(
                        mod.DisplayName,
                        incomingModName,
                        StringComparison.OrdinalIgnoreCase));

            if (existingMod != null)
            {
                MessageBox.Show(
                    $"{existingMod.DisplayName} is already in your library.\n\n" +
                    "Remove the existing copy before importing it again.",
                    "Mod already installed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            ImportModButton.IsEnabled = false;

            ImportModButton.IsEnabled = false;
            ImportModButton.Content = "IMPORTING...";

            try
            {
                // Large archives are processed in the background so
                // the interface remains responsive during the import.
                InstalledMod installedMod =
                    await Task.Run(() =>
                        _modLibraryService.Import(
                            fileDialog.FileName));

                _settings.InstalledMods.Add(
                    installedMod);

                _settingsService.Save(_settings);

                RefreshLibrarySummary();

                MessageBox.Show(
                    $"{installedMod.DisplayName} was added to your library.\n\n" +
                    $"Package files: {installedMod.PackageFiles.Count}\n" +
                    $"Assets detected: {installedMod.AssetPackages.Count}\n" +
                    $"Live-refreshable: " +
                    $"{installedMod.AssetPackages.Count(package => package.IsSafeForLiveReload)}",
                    "Mod imported",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"Limelight could not import this mod.\n\n{exception.Message}",
                    "Import failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                ImportModButton.IsEnabled = true;
                ImportModButton.Content = "IMPORT MOD";
            }
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

            if (installedCount == 0)
            {
                LibrarySummaryText.Text =
                    "Your mod library is empty. Import a ZIP archive or browse Nexus Mods to get started.";

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

        private async void LaunchGame_Click(
    object sender,
    RoutedEventArgs e)
        {
            string? gameDirectory =
                _gameDirectory;

            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                MessageBox.Show(
                    "Connect Limelight to your Dead as Disco folder first.",
                    "Game not connected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            if (_gameProcessService.IsGameRunning(gameDirectory))
            {
                // Starting a second copy can cause Steam or the game to display
                // confusing errors, so keep the already-running instance.
                MessageBox.Show(
                    "Dead as Disco is already running.",
                    "Game already running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            string executablePath =
                Path.Combine(
                    gameDirectory,
                    "Pagoda.exe");

            if (!File.Exists(executablePath))
            {
                MessageBox.Show(
                    "Limelight could not find Pagoda.exe.\n\n" +
                    "Reconnect the game folder in Settings and try again.",
                    "Game executable missing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                Ue4ssDetectionResult loader =
                    _ue4ssDetectionService.Detect(
                        gameDirectory);

                if (loader.IsInstalled &&
                    _ue4ssConfigurationService.IsRuntimeCompatible(loader) &&
                    _liveLoaderBridgeService.HasBridgeFiles(loader))
                {
                    try
                    {
                        // Repair the managed settings, signatures and enable
                        // line in case another mod tool changed them while
                        // Limelight was already open.
                        _ue4ssConfigurationService.Apply(loader);
                        _liveLoaderBridgeService.EnsureInstalled(loader);
                    }
                    catch
                    {
                        // The live loader is optional, so a repair problem
                        // should never prevent the user launching the game.
                    }
                }

                ProcessStartInfo startInfo =
    new ProcessStartInfo
    {
        // Launch through Steam so Pagoda.exe is not mistaken for
        // a custom command-line argument.
        FileName = "steam://rungameid/3404260",
        UseShellExecute = true
    };

                // A fresh game launch must produce a fresh heartbeat before the dashboard
                // is allowed to report the bridge as online.
                _liveLoaderBridgeService.ClearHeartbeat();

                // Ask Steam to launch its registered Dead as Disco installation.
                Process.Start(startInfo);

                // Keep Limelight locked while the runtime comes online and the
                // active mod is mounted. This removes the tempting-but-unsafe
                // window where a user can switch mods during LoadMap.
                await InitialiseLiveLoaderForRunningGameAsync(
                    waitForGameProcess: true);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "Dead as Disco could not be started.\n\n" +
                    exception.Message,
                    "Launch failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
            var folderDialog = new OpenFolderDialog
            {
                Title = "Choose the Dead as Disco installation folder",
                Multiselect = false
            };

            // Cancelling leaves the current connection unchanged.
            if (folderDialog.ShowDialog() != true)
            {
                return;
            }

            string selectedDirectory =
                folderDialog.FolderName;

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

            MessageBox.Show(
                "Dead as Disco was connected successfully.",
                "Limelight",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

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
                    MessageBox.Show(
                        "Limelight could not find Pagoda.exe and the game's Paks folder.\n\n" +
                        "Select the main Dead as Disco folder, not the Paks folder itself.",
                        "Invalid game folder",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
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
