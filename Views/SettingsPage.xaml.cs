using Limelight.Models;
using Limelight.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Limelight.Views
{
    public partial class SettingsPage : UserControl
    {
        public event Action? RepairRequested;
        public event Action? ExportDiagnosticsRequested;
        public event Action? ChangeGameFolderRequested;
        public event Action? NativeTestRequested;

        public SettingsPage()
        {
            InitializeComponent();
        }

        public void ShowStatus(
            string? gameDirectory,
            bool isGameRunning,
            LiveSessionState session,
            LiveSessionCleanupResult stagingSnapshot)
        {
            bool isConnected =
                !string.IsNullOrWhiteSpace(gameDirectory);

            GameConnectionStatusText.Text =
                isConnected
                    ? "CONNECTED"
                    : "NOT CONNECTED";

            GameConnectionStatusText.Foreground =
                StatusBrush(isConnected);

            GameConnectionDetailText.Text =
                isConnected
                    ? gameDirectory
                    : "Choose the Dead as Disco installation folder.";

            GameRunningBadgeText.Text =
                isGameRunning
                    ? "RUNNING"
                    : "NOT RUNNING";

            GameRunningBadgeText.Foreground =
                StatusBrush(isGameRunning);

            SessionStatusText.Text =
                session.Status.ToString().ToUpperInvariant();

            bool sessionHealthy =
                session.Status is LiveSessionStatus.Active or
                    LiveSessionStatus.Initialising or
                    LiveSessionStatus.Switching;

            SessionStatusText.Foreground =
                StatusBrush(sessionHealthy);

            int mountedContainers =
                session.Mounts.Count;

            MountedContainerText.Text =
                $"{mountedContainers} / {LiveSessionService.MaximumMountedContainers} CONTAINERS";

            MountedContainerText.Foreground =
                StatusBrush(
                    mountedContainers <
                    LiveSessionService.MaximumMountedContainers);

            SessionDetailText.Text =
                CreateSessionDetail(
                    session,
                    isGameRunning);

            StagingText.Text =
                stagingSnapshot.DeletedFileCount == 0
                    ? "No staged files are waiting for cleanup."
                    : $"{stagingSnapshot.DeletedFileCount} staged file(s), " +
                      $"{FormatBytes(stagingSnapshot.DeletedBytes)}. Limelight cleans these after the game closes.";

            RepairButton.IsEnabled =
                isConnected &&
                !isGameRunning;

            RepairButton.Opacity =
                RepairButton.IsEnabled
                    ? 1
                    : 0.45;

            ChangeGameFolderButton.IsEnabled =
                !isGameRunning;

            ChangeGameFolderButton.Opacity =
                ChangeGameFolderButton.IsEnabled
                    ? 1
                    : 0.45;

            NativeTestButton.IsEnabled =
                isConnected &&
                isGameRunning;

            NativeTestButton.Opacity =
                NativeTestButton.IsEnabled
                    ? 1
                    : 0.45;
        }

        private string CreateSessionDetail(
            LiveSessionState session,
            bool isGameRunning)
        {
            if (!string.IsNullOrWhiteSpace(session.LastRecoveryMessage) &&
                !isGameRunning)
            {
                return session.LastRecoveryMessage;
            }

            if (!string.IsNullOrWhiteSpace(session.LastError))
            {
                return $"Last live change: {session.LastError}";
            }

            if (!string.IsNullOrWhiteSpace(session.ActiveModName) &&
                isGameRunning)
            {
                return $"{session.ActiveModName} is active. " +
                       $"{session.SuccessfulSwitches} successful live switch(es) this session.";
            }

            return isGameRunning
                ? "The live session is ready for a model change."
                : "Start the game to begin a fresh live session.";
        }

        private Brush StatusBrush(
            bool isHealthy)
        {
            return (Brush)FindResource(
                isHealthy
                    ? "CyanBrush"
                    : "PinkBrush");
        }

        private static string FormatBytes(
            long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }

            double kilobytes =
                bytes / 1024d;

            if (kilobytes < 1024)
            {
                return $"{kilobytes:F1} KB";
            }

            return $"{kilobytes / 1024d:F1} MB";
        }

        private void RepairButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            RepairRequested?.Invoke();
        }

        private void ExportButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ExportDiagnosticsRequested?.Invoke();
        }

        private void ChangeGameFolderButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ChangeGameFolderRequested?.Invoke();
        }

        private void NativeTestButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            NativeTestRequested?.Invoke();
        }
    }
}
