using Limelight.Models;
using Limelight.Services;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Limelight
{
    public partial class MainWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly AppSettings _settings;
        private readonly ModLibraryService _modLibraryService;

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

            ImportModButton.IsEnabled = false;
            ImportModButton.Content = "IMPORTING...";

            try
            {
                // Large archives are copied in the background so Limelight
                // does not appear frozen during the import.
                InstalledMod installedMod =
                    await Task.Run(() =>
                        _modLibraryService.Import(
                            fileDialog.FileName));

                _settings.InstalledMods.Add(installedMod);
                _settingsService.Save(_settings);

                RefreshLibrarySummary();

                MessageBox.Show(
                    $"{installedMod.Name} was added to your library.\n\n" +
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
            // Only count mods whose library folder still exists.
            int installedCount =
                _settings.InstalledMods.Count(mod =>
                    Directory.Exists(mod.InstallDirectory));

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

        private void ConnectGame_Click(object sender, RoutedEventArgs e)
        {
            // Ask for the game's main folder rather than making the user
            // manually navigate to its internal Paks directory.
            var folderDialog = new OpenFolderDialog
            {
                Title = "Choose the Dead as Disco installation folder",
                Multiselect = false
            };

            // Cancelling should leave the current connection unchanged.
            if (folderDialog.ShowDialog() != true)
            {
                return;
            }

            string selectedDirectory = folderDialog.FolderName;

            if (!TryConnectToGame(selectedDirectory, showError: true))
            {
                return;
            }

            // Save only after the directory has passed all validation checks.
            _settings.GameDirectory = selectedDirectory;
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

            // Checking both locations helps avoid accepting an unrelated
            // folder that happens to contain a file named Pagoda.exe.
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

            _gameDirectory = selectedDirectory;

            // Update the dashboard only after the installation is confirmed.
            GameStatusDot.Fill =
                (Brush)FindResource("LimeBrush");

            GameStatusTitle.Text = "GAME CONNECTED";
            GameStatusDescription.Text = selectedDirectory;
            ConnectGameButton.Content = "CHANGE FOLDER";

            return true;
        }

        private void RestoreSavedGameDirectory()
        {
            if (string.IsNullOrWhiteSpace(_settings.GameDirectory))
            {
                return;
            }

            // Game updates or Steam library moves can make a previously valid
            // directory disappear, so it is checked again on every launch.
            if (TryConnectToGame(
                    _settings.GameDirectory,
                    showError: false))
            {
                return;
            }

            _settings.GameDirectory = string.Empty;
            _settingsService.Save(_settings);

            GameStatusDescription.Text =
                "The previously selected game folder could not be found.";
        }
    }
}