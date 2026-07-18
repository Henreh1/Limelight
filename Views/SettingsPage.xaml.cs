using Limelight.Models;
using Limelight.Services;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Limelight.Views
{
    public partial class SettingsPage : UserControl
    {
        private bool _isCapturingX19Hotkey;

        public event Action? RepairRequested;
        public event Action? ExportDiagnosticsRequested;
        public event Action? ChangeGameFolderRequested;
        public event Action? NativeTestRequested;
        public event Action<string>? NexusConnectRequested;
        public event Action? NexusDisconnectRequested;
        public event Action<string>? X19HotkeyChanged;

        public SettingsPage()
        {
            InitializeComponent();
        }

        public void ShowX19Hotkey(
            string hotkeyGesture)
        {
            if (_isCapturingX19Hotkey)
            {
                return;
            }

            X19HotkeyText.Text =
                string.IsNullOrWhiteSpace(hotkeyGesture)
                    ? "NOT SET"
                    : hotkeyGesture.ToUpperInvariant();
        }

        private void CaptureX19Hotkey_Click(
            object sender,
            RoutedEventArgs e)
        {
            _isCapturingX19Hotkey = true;

            CaptureX19HotkeyButton.Content =
                "PRESS A KEY";

            X19HotkeyStatusText.Text =
                "Press the new key combination now. Press Escape to cancel.";

            CaptureX19HotkeyButton.Focus();
            Keyboard.Focus(CaptureX19HotkeyButton);
        }

        private void CaptureX19Hotkey_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (!_isCapturingX19Hotkey)
            {
                return;
            }

            e.Handled = true;

            Key pressedKey =
                e.Key == Key.System
                    ? e.SystemKey
                    : e.Key;

            if (pressedKey == Key.Escape)
            {
                FinishHotkeyCapture(
                    X19HotkeyText.Text,
                    saveChange: false);

                return;
            }

            if (IsModifierKey(pressedKey) ||
                pressedKey == Key.None)
            {
                X19HotkeyStatusText.Text =
                    "Add a letter, number, or function key to the combination.";

                return;
            }

            ModifierKeys modifiers =
                Keyboard.Modifiers &
                (ModifierKeys.Control |
                 ModifierKeys.Alt |
                 ModifierKeys.Shift);

            string gesture =
                CreateGestureText(
                    pressedKey,
                    modifiers);

            FinishHotkeyCapture(
                gesture,
                saveChange: true);
        }

        private void FinishHotkeyCapture(
            string gesture,
            bool saveChange)
        {
            _isCapturingX19Hotkey = false;

            CaptureX19HotkeyButton.Content =
                "CHANGE HOTKEY";

            X19HotkeyStatusText.Text =
                saveChange
                    ? "The X19 hotkey is ready and will only work while Dead as Disco is selected."
                    : "The existing X19 hotkey was kept.";

            if (!saveChange)
            {
                return;
            }

            X19HotkeyText.Text =
                gesture;

            X19HotkeyChanged?.Invoke(gesture);
        }

        private static bool IsModifierKey(
            Key key)
        {
            return key is
                Key.LeftCtrl or
                Key.RightCtrl or
                Key.LeftAlt or
                Key.RightAlt or
                Key.LeftShift or
                Key.RightShift or
                Key.LWin or
                Key.RWin;
        }

        private static string CreateGestureText(
            Key key,
            ModifierKeys modifiers)
        {
            List<string> parts =
                new();

            if (modifiers.HasFlag(ModifierKeys.Control))
            {
                parts.Add("CTRL");
            }

            if (modifiers.HasFlag(ModifierKeys.Alt))
            {
                parts.Add("ALT");
            }

            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                parts.Add("SHIFT");
            }

            parts.Add(
                key switch
                {
                    Key.D0 => "0",
                    Key.D1 => "1",
                    Key.D2 => "2",
                    Key.D3 => "3",
                    Key.D4 => "4",
                    Key.D5 => "5",
                    Key.D6 => "6",
                    Key.D7 => "7",
                    Key.D8 => "8",
                    Key.D9 => "9",
                    _ => key.ToString().ToUpperInvariant()
                });

            return string.Join(
                "+",
                parts);
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
                LiveSessionService.CountMountedContainers(
                    session);

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

        public void ShowNexusStatus(
    bool isConnected,
    string? accountName,
    bool isBusy = false)
        {
            bool healthy =
                isConnected ||
                isBusy;

            NexusConnectionStatusText.Text =
                isBusy
                    ? "CONNECTING"
                    : isConnected
                        ? "CONNECTED"
                        : "NOT CONNECTED";

            NexusConnectionStatusText.Foreground =
                StatusBrush(healthy);

            string displayName =
                string.IsNullOrWhiteSpace(accountName)
                    ? "Your Nexus account"
                    : accountName;

            NexusAccountDetailText.Text =
                isBusy
                    ? "Limelight is checking your Nexus API key."
                    : isConnected
                        ? $"{displayName} is connected. Nexus browsing and downloads are ready."
                        : "Connect a personal API key to test browsing and downloads inside Limelight.";

            NexusApiKeyBox.IsEnabled =
                !isConnected &&
                !isBusy;

            NexusConnectButton.IsEnabled =
                !isConnected &&
                !isBusy;

            NexusDisconnectButton.IsEnabled =
                isConnected &&
                !isBusy;

            NexusConnectButton.Opacity =
                NexusConnectButton.IsEnabled
                    ? 1
                    : 0.45;

            NexusDisconnectButton.Opacity =
                NexusDisconnectButton.IsEnabled
                    ? 1
                    : 0.45;

            NexusAccessBadgeText.Text =
                isBusy
                    ? "CHECKING"
                    : isConnected
                        ? "API READY"
                        : "TESTING ACCESS";

            NexusAccessBadgeText.Foreground =
                StatusBrush(healthy);

            if (isConnected)
            {
                // Once the key has been accepted there is no reason to leave it visible.
                NexusApiKeyBox.Password =
                    string.Empty;
            }
        }

        public void ShowNexusUsage(
    NexusApiUsageSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            NexusSessionRequestCountText.Text =
                snapshot.RequestsThisSession.ToString("N0");

            NexusHourlyRemainingText.Text =
                snapshot.HourlyRemaining?.ToString("N0") ??
                "UNKNOWN";

            NexusDailyRemainingText.Text =
                snapshot.DailyRemaining?.ToString("N0") ??
                "UNKNOWN";

            NexusLastRequestText.Text =
                snapshot.LastRequestUtc.HasValue
                    ? $"{snapshot.LastRequestKind} • " +
                      $"{snapshot.LastRequestUtc.Value.ToLocalTime():dd MMM yyyy HH:mm:ss}"
                    : "NONE YET";

            bool testingIsSafe =
                snapshot.HasQuotaInformation &&
                !snapshot.ShouldPauseRequests;

            NexusTestingStatusText.Text =
                snapshot.ShouldPauseRequests
                    ? "REQUESTS PAUSED"
                    : snapshot.HasQuotaInformation
                        ? "WITHIN TEST LIMITS"
                        : "WAITING FOR QUOTA";

            // I use the same colour language as the rest of Limelight.
            // Cyan means testing is safe, while pink needs attention.
            NexusTestingStatusText.Foreground =
                StatusBrush(testingIsSafe);

            NexusHourlyRemainingText.Foreground =
                StatusBrush(testingIsSafe);

            NexusDailyRemainingText.Foreground =
                StatusBrush(testingIsSafe);
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

        private void NexusConnectButton_Click(
    object sender,
    RoutedEventArgs e)
        {
            string apiKey =
                NexusApiKeyBox.Password.Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                NexusConnectionStatusText.Text =
                    "API KEY REQUIRED";

                NexusConnectionStatusText.Foreground =
                    StatusBrush(isHealthy: false);

                NexusAccountDetailText.Text =
                    "Paste your personal Nexus Mods API key before connecting.";

                return;
            }

            ShowNexusStatus(
                isConnected: false,
                accountName: null,
                isBusy: true);

            NexusConnectRequested?.Invoke(apiKey);
        }

        private void NexusDisconnectButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            NexusDisconnectRequested?.Invoke();
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
