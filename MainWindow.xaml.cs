using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Limelight
{
    public partial class MainWindow : Window
    {
        // Keep the selected path available for launching the game and managing mods later.
        private string? _gameDirectory;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void ConnectGame_Click(object sender, RoutedEventArgs e)
        {
            // Ask for the game's main folder rather than making the user find the Paks folder. Its so nice to be polite :D
            var folderDialog = new OpenFolderDialog
            {
                Title = "Choose the Dead as Disco installation folder",
                Multiselect = false
            };

            // Closing or cancelling the dialog should leave the current connection unchanged.
            if (folderDialog.ShowDialog() != true)
            {
                return;
            }

            string selectedDirectory = folderDialog.FolderName;
            string gameExecutable = Path.Combine(
                selectedDirectory,
                "Pagoda.exe");

            string pakDirectory = Path.Combine(
                selectedDirectory,
                "Pagoda",
                "Content",
                "Paks");

            // Both locations are checked to avoid accepting an unrelated Pagoda.exe file.
            if (!File.Exists(gameExecutable) ||
                !Directory.Exists(pakDirectory))
            {
                MessageBox.Show(
                    "Limelight could not find Pagoda.exe and the game's Paks folder.\n\n" +
                    "Select the main Dead as Disco folder, not the Paks folder itself.",
                    "Invalid game folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            _gameDirectory = selectedDirectory;

            // Give the user a clear visual confirmation that Limelight is connected.
            GameStatusDot.Fill =
                (Brush)FindResource("LimeBrush");

            GameStatusTitle.Text = "GAME CONNECTED";
            GameStatusDescription.Text = selectedDirectory;
            ConnectGameButton.Content = "CHANGE FOLDER";

            MessageBox.Show(
                "Dead as Disco was connected successfully.",
                "Limelight",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}