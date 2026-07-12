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

        private string? _gameDirectory;

        public MainWindow()
        {
            InitializeComponent();

            _settingsService = new SettingsService();
            _settings = _settingsService.Load();

            RestoreSavedGameDirectory();
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