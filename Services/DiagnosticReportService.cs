using Limelight.Models;
using System.IO;
using System.Reflection;
using System.Text;

namespace Limelight.Services
{
    public sealed class DiagnosticReportService
    {
        public string CreateReport(
            AppSettings settings,
            LiveSessionState session,
            string? gameDirectory,
            bool isGameRunning,
            Ue4ssDetectionResult loader,
            LiveSessionCleanupResult stagingSnapshot)
        {
            var report =
                new StringBuilder();

            Version? version =
                Assembly.GetEntryAssembly()
                    ?.GetName()
                    .Version;

            InstalledMod? activeMod =
                settings.InstalledMods.FirstOrDefault(mod =>
                    string.Equals(
                        mod.Id,
                        settings.ActiveModId,
                        StringComparison.OrdinalIgnoreCase));

            report.AppendLine("LIMELIGHT DIAGNOSTIC REPORT");
            report.AppendLine("===========================");
            report.AppendLine($"Created (UTC): {DateTimeOffset.UtcNow:O}");
            report.AppendLine($"Limelight version: {version?.ToString() ?? "Unknown"}");
            report.AppendLine($"Windows: {Environment.OSVersion}");
            report.AppendLine($".NET: {Environment.Version}");
            report.AppendLine();

            report.AppendLine("APPLICATION");
            report.AppendLine($"Game connected: {!string.IsNullOrWhiteSpace(gameDirectory)}");
            report.AppendLine($"Game running: {isGameRunning}");
            report.AppendLine($"Installed mods: {settings.InstalledMods.Count}");
            report.AppendLine($"Active mod: {activeMod?.DisplayName ?? "None"}");
            report.AppendLine($"Pending deployment: {!string.IsNullOrWhiteSpace(settings.PendingDeploymentModId)}");
            report.AppendLine();

            report.AppendLine("LIVE LOADER");
            report.AppendLine($"UE4SS installed: {loader.IsInstalled}");
            report.AppendLine($"UE4SS partial install: {loader.IsPartiallyInstalled}");
            report.AppendLine($"Runtime compatible: {SafeRuntimeCompatibility(loader)}");
            report.AppendLine($"Lua bridge installed: {SafeBridgeInstalled(loader)}");
            report.AppendLine($"Lua bridge online: {SafeBridgeOnline()}");
            report.AppendLine();

            report.AppendLine("LIVE SESSION");
            report.AppendLine($"Session: {ShortSessionId(session.SessionId)}");
            report.AppendLine($"Status: {session.Status}");
            report.AppendLine($"Activation in progress: {session.ActivationInProgress}");
            report.AppendLine($"Successful switches: {session.SuccessfulSwitches}");
            report.AppendLine($"Mounted containers recorded: {session.Mounts.Count(record => record.WasMounted)}");
            report.AppendLine($"Staged files: {stagingSnapshot.DeletedFileCount}");
            report.AppendLine($"Staged bytes: {stagingSnapshot.DeletedBytes}");
            report.AppendLine($"Last error: {ValueOrNone(session.LastError)}");
            report.AppendLine($"Last recovery: {ValueOrNone(session.LastRecoveryMessage)}");

            foreach (LiveSessionMountRecord mount in session.Mounts)
            {
                report.AppendLine(
                    $"  Container: mod={mount.ModName}; file={Path.GetFileName(mount.PakPath)}; " +
                    $"order={mount.MountOrder}; mounted={mount.WasMounted}; time={mount.MountedAt:O}");
            }

            report.AppendLine();
            report.AppendLine("RECENT UE4SS EVENTS");
            AppendRelevantLogLines(
                report,
                loader.LogPath,
                gameDirectory);

            return RedactPaths(
                report.ToString(),
                gameDirectory);
        }

        private static string ShortSessionId(
            string sessionId)
        {
            return string.IsNullOrWhiteSpace(sessionId)
                ? "None"
                : sessionId[..Math.Min(8, sessionId.Length)];
        }

        private static string ValueOrNone(
            string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "None"
                : value;
        }

        private static void AppendRelevantLogLines(
            StringBuilder report,
            string logPath,
            string? gameDirectory)
        {
            if (string.IsNullOrWhiteSpace(logPath) ||
                !File.Exists(logPath))
            {
                report.AppendLine("UE4SS log was not available.");
                return;
            }

            try
            {
                string[] relevantLines =
                    File.ReadLines(logPath)
                        .Where(line =>
                            line.Contains("limelight", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("exception", StringComparison.OrdinalIgnoreCase))
                        .TakeLast(200)
                        .ToArray();

                if (relevantLines.Length == 0)
                {
                    report.AppendLine("No matching warning or Limelight events were found.");
                    return;
                }

                foreach (string line in relevantLines)
                {
                    report.AppendLine(
                        RedactPaths(
                            line,
                            gameDirectory));
                }
            }
            catch (Exception exception)
            {
                report.AppendLine(
                    $"UE4SS log could not be read: {exception.Message}");
            }
        }

        private static string RedactPaths(
            string text,
            string? gameDirectory)
        {
            var replacements =
                new List<KeyValuePair<string, string>>
                {
                    new(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "<LOCAL_APP_DATA>"),
                    new(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.UserProfile),
                        "<USER_PROFILE>")
                };

            if (!string.IsNullOrWhiteSpace(gameDirectory))
            {
                replacements.Add(
                    new KeyValuePair<string, string>(
                        gameDirectory,
                        "<GAME_DIRECTORY>"));
            }

            string redacted = text;

            foreach ((string path, string replacement) in
                     replacements.OrderByDescending(item =>
                         item.Key.Length))
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    redacted = redacted.Replace(
                        path,
                        replacement,
                        StringComparison.OrdinalIgnoreCase);
                }
            }

            return redacted;
        }

        private static bool SafeRuntimeCompatibility(
            Ue4ssDetectionResult loader)
        {
            try
            {
                return new DeadAsDiscoUe4ssConfigurationService()
                    .IsRuntimeCompatible(loader);
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeBridgeInstalled(
            Ue4ssDetectionResult loader)
        {
            try
            {
                return new LiveLoaderBridgeService()
                    .IsInstalled(loader);
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeBridgeOnline()
        {
            try
            {
                return new LiveLoaderBridgeService()
                    .IsOnline();
            }
            catch
            {
                return false;
            }
        }
    }
}
