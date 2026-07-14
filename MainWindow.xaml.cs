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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;

namespace Limelight
{
    public partial class MainWindow : Window
    {
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
        private readonly DispatcherTimer _gameStatusTimer;
        private bool _hasHandledLiveLoaderPrompt;
        private bool _isLiveLoaderSetupRunning;

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

            _settings =
                _settingsService.Load();

            // The page reports its button clicks to the main window, where
            // the settings and connected game directory are available.
            MyModsPageControl.ToggleModRequested +=
                ToggleModRequested;

            MyModsPageControl.RemoveModRequested +=
                RemoveModRequested;

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
            _gameStatusTimer.Start();

            // Finish any existing-mod migration before opening another modal window.
            await CheckForExistingMods();
            await ShowLiveLoaderSetupPromptIfNeeded();
        }

        private void GameStatusTimer_Tick(
            object? sender,
            EventArgs e)
        {
            UpdateGameRunningStatus();
        }

        private void MainWindow_Closed(
            object? sender,
            EventArgs e)
        {
            // The timer belongs to this window, so there is no reason to leave
            // it checking processes after Limelight has closed.
            _gameStatusTimer.Stop();
        }

        private void UpdateGameRunningStatus()
        {
            if (_isLiveLoaderSetupRunning)
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

                LiveLoaderStatusText.Text =
                    "NOT CONNECTED";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("MutedTextBrush");

                return;
            }

            bool isGameRunning =
                _gameProcessService.IsGameRunning(
                    gameDirectory);

            if (isGameRunning)
            {
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
                LiveLoaderStatusText.Text =
                    "REPAIR NEEDED";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("PinkBrush");

                return;
            }

            if (!loader.IsInstalled)
            {
                LiveLoaderStatusText.Text =
                    "NOT INSTALLED";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("PinkBrush");

                return;
            }

            if (!_ue4ssConfigurationService.IsConfigured(loader))
            {
                LiveLoaderStatusText.Text =
                    "SETUP NEEDED";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("PinkBrush");

                return;
            }

            if (!_liveLoaderBridgeService.IsInstalled(loader))
            {
                LiveLoaderStatusText.Text =
                    "BRIDGE NEEDED";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("PinkBrush");

                return;
            }

            if (!isGameRunning)
            {
                LiveLoaderStatusText.Text =
                    "READY";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("CyanBrush");

                return;
            }

            if (_liveLoaderBridgeService.IsOnline())
            {
                LiveLoaderStatusText.Text =
                    "ONLINE";

                LiveLoaderStatusText.Foreground =
                    (Brush)FindResource("CyanBrush");

                return;
            }

            // UE4SS is installed and the game exists, but the Lua bridge has not
            // produced a recent heartbeat.
            LiveLoaderStatusText.Text =
                "OFFLINE";

            LiveLoaderStatusText.Foreground =
                (Brush)FindResource("PinkBrush");
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

        private async void ToggleModRequested(string modId)
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

            if (string.IsNullOrWhiteSpace(_gameDirectory))
            {
                MessageBox.Show(
                    "Connect the Dead as Disco installation before activating a mod.",
                    "Game not connected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            string gameDirectory =
                _gameDirectory;

            bool isCurrentlyActive =
                string.Equals(
                    _settings.ActiveModId,
                    selectedMod.Id,
                    StringComparison.OrdinalIgnoreCase);

            try
            {
                if (isCurrentlyActive)
                {
                    await Task.Run(() =>
                        _modDeploymentService.Deactivate(
                            gameDirectory));

                    _settings.ActiveModId =
                        string.Empty;
                }
                else
                {
                    await Task.Run(() =>
                        _modDeploymentService.Activate(
                            selectedMod,
                            gameDirectory));

                    _settings.ActiveModId =
                        selectedMod.Id;
                }

                _settingsService.Save(_settings);
                RefreshLibrarySummary();

                string message =
                    isCurrentlyActive
                        ? $"{selectedMod.DisplayName} was deactivated."
                        : $"{selectedMod.DisplayName} was activated on disk.";

                MessageBox.Show(
                    message,
                    "Limelight",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"Limelight could not change the active mod.\n\n{exception.Message}",
                    "Mod activation failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
            // Refresh before displaying the page so newly imported
            // mods appear without restarting Limelight.
            RefreshLibrarySummary();

            DashboardPage.Visibility =
                Visibility.Collapsed;

            MyModsPageControl.Visibility =
                Visibility.Visible;

            SetSelectedNavigation(showMyMods: true);
        }

        private void ShowDashboard_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            MyModsPageControl.Visibility =
                Visibility.Collapsed;

            DashboardPage.Visibility =
                Visibility.Visible;

            SetSelectedNavigation(showMyMods: false);
        }

        private void SetSelectedNavigation(bool showMyMods)
        {
            // Keep both borders the same size and only swap their colours.
            // This prevents the navigation text from shifting when selected.
            Brush activeBackground =
                new SolidColorBrush(
                    Color.FromRgb(37, 32, 59));

            Brush pink =
                (Brush)FindResource("PinkBrush");

            Brush normalText =
                (Brush)FindResource("TextBrush");

            Brush mutedText =
                (Brush)FindResource("MutedTextBrush");

            DashboardNavigation.Background =
                showMyMods
                    ? Brushes.Transparent
                    : activeBackground;

            DashboardNavigation.BorderBrush =
                showMyMods
                    ? Brushes.Transparent
                    : pink;

            DashboardNavigationText.Foreground =
                showMyMods
                    ? mutedText
                    : normalText;

            MyModsNavigation.Background =
                showMyMods
                    ? activeBackground
                    : Brushes.Transparent;

            MyModsNavigation.BorderBrush =
                showMyMods
                    ? pink
                    : Brushes.Transparent;

            MyModsNavigationText.Foreground =
                showMyMods
                    ? normalText
                    : mutedText;
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
                    $"Package files: {installedMod.PackageFiles.Count}",
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

        private void LaunchGame_Click(
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

            ConnectGameButton.Content =
                "CHANGE FOLDER";

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
