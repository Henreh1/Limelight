using Limelight.Models;
using Limelight.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Limelight
{
    public partial class MainWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly ModLibraryService _modLibraryService;
        private readonly AppSettings _settings;

        private string? _gameDirectory;

        public MainWindow()
        {
            InitializeComponent();

            _settingsService = new SettingsService();
            _modLibraryService = new ModLibraryService();
            _settings = _settingsService.Load();

            RestoreSavedGameDirectory();
            RefreshLibrarySummary();
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
            // Ignore library entries whose extracted folder was
            // manually removed outside Limelight.
            List<InstalledMod> availableMods =
                _settings.InstalledMods
                    .Where(mod =>
                        Directory.Exists(
                            mod.InstallDirectory))
                    .ToList();

            int installedCount =
                availableMods.Count;

            // Keep the dashboard and My Mods page on the same snapshot.
            MyModsPageControl.ShowMods(
                availableMods);

            InstalledModCountText.Text =
                installedCount.ToString();

            if (installedCount == 0)
            {
                LibrarySummaryText.Text =
                    "Your mod library is empty. Import a ZIP archive or browse Nexus Mods to get started.";

                LibraryStatusText.Text =
                    "NO MODS YET";

                return;
            }

            LibrarySummaryText.Text =
                installedCount == 1
                    ? "1 mod is installed and ready to activate."
                    : $"{installedCount} mods are installed and ready to activate.";

            LibraryStatusText.Text =
                $"{installedCount} READY";
        }

        private void ConnectGame_Click(
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
                (Brush)FindResource("LimeBrush");

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