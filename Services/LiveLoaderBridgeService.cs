using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Limelight.Services
{
    public sealed class LiveLoaderBridgeService
    {
        private const string BridgeName =
            "LimelightBridge";

        private const string BridgeScript =
            """
            local localAppData = os.getenv("LOCALAPPDATA")

            if localAppData == nil then
                print("[LimelightBridge] LOCALAPPDATA could not be found\n")
                return
            end

            local runtimeDirectory =
                localAppData .. "\\Limelight\\Runtime"

            local heartbeatPath =
                runtimeDirectory .. "\\heartbeat.txt"

            local function writeHeartbeat()
                local heartbeatFile =
                    io.open(heartbeatPath, "w")

                if heartbeatFile == nil then
                    return
                end

                heartbeatFile:write(
                    tostring(os.time()))

                heartbeatFile:close()
            end

            -- Write immediately so Limelight does not need to wait for the
            -- first timer interval before recognising the bridge.
            writeHeartbeat()

            LoopAsync(1000, function()
                writeHeartbeat()

                -- Returning false keeps the heartbeat loop running.
                return false
            end)

            print("[LimelightBridge] Runtime bridge online\n")
            """;

        public string RuntimeDirectory =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Limelight",
                "Runtime");

        public string HeartbeatPath =>
            Path.Combine(
                RuntimeDirectory,
                "heartbeat.txt");

        public void EnsureInstalled(
            Ue4ssDetectionResult installation)
        {
            if (!installation.IsInstalled)
            {
                throw new InvalidOperationException(
                    "UE4SS must be installed before adding the Limelight bridge.");
            }

            if (string.IsNullOrWhiteSpace(
                    installation.ModsDirectory))
            {
                throw new DirectoryNotFoundException(
                    "The UE4SS Mods directory could not be determined.");
            }

            Directory.CreateDirectory(
                RuntimeDirectory);

            string scriptsDirectory =
                Path.Combine(
                    installation.ModsDirectory,
                    BridgeName,
                    "scripts");

            Directory.CreateDirectory(
                scriptsDirectory);

            string scriptPath =
                Path.Combine(
                    scriptsDirectory,
                    "main.lua");

            WriteScriptIfChanged(
                scriptPath);

            string modsTextPath =
                Path.Combine(
                    installation.ModsDirectory,
                    "mods.txt");

            EnableBridgeInModsFile(
                modsTextPath);
        }

        public bool IsInstalled(
            Ue4ssDetectionResult installation)
        {
            if (!installation.IsInstalled ||
                string.IsNullOrWhiteSpace(
                    installation.ModsDirectory))
            {
                return false;
            }

            string scriptPath =
                Path.Combine(
                    installation.ModsDirectory,
                    BridgeName,
                    "scripts",
                    "main.lua");

            string modsTextPath =
                Path.Combine(
                    installation.ModsDirectory,
                    "mods.txt");

            if (!File.Exists(scriptPath) ||
                !File.Exists(modsTextPath))
            {
                return false;
            }

            try
            {
                return File.ReadLines(modsTextPath)
                    .Any(IsEnabledBridgeLine);
            }
            catch
            {
                return false;
            }
        }

        public bool IsOnline()
        {
            try
            {
                if (!File.Exists(HeartbeatPath))
                {
                    return false;
                }

                DateTime lastHeartbeat =
                    File.GetLastWriteTimeUtc(
                        HeartbeatPath);

                TimeSpan heartbeatAge =
                    DateTime.UtcNow -
                    lastHeartbeat;

                // The bridge writes once per second. Five seconds leaves room
                // for a loading screen or a short frame-rate stall.
                return heartbeatAge >=
                           TimeSpan.FromSeconds(-2) &&
                       heartbeatAge <=
                           TimeSpan.FromSeconds(5);
            }
            catch
            {
                return false;
            }
        }

        public void ClearHeartbeat()
        {
            try
            {
                if (File.Exists(HeartbeatPath))
                {
                    File.Delete(HeartbeatPath);
                }
            }
            catch
            {
                // A stale heartbeat naturally expires after five seconds, so
                // failing to remove it is harmless.
            }
        }

        private static void WriteScriptIfChanged(
            string scriptPath)
        {
            if (File.Exists(scriptPath))
            {
                string existingScript =
                    File.ReadAllText(scriptPath);

                if (string.Equals(
                        existingScript,
                        BridgeScript,
                        StringComparison.Ordinal))
                {
                    return;
                }
            }

            // Limelight owns this one script, so updating it does not affect
            // any other UE4SS mods the user has installed.
            File.WriteAllText(
                scriptPath,
                BridgeScript);
        }

        private static void EnableBridgeInModsFile(
            string modsTextPath)
        {
            List<string> lines =
                File.Exists(modsTextPath)
                    ? File.ReadAllLines(modsTextPath).ToList()
                    : new List<string>();

            int existingLineIndex =
                lines.FindIndex(IsBridgeLine);

            if (existingLineIndex >= 0)
            {
                if (IsEnabledBridgeLine(
                        lines[existingLineIndex]))
                {
                    return;
                }

                lines[existingLineIndex] =
                    $"{BridgeName} : 1";
            }
            else
            {
                if (lines.Count > 0 &&
                    !string.IsNullOrWhiteSpace(lines[^1]))
                {
                    lines.Add(string.Empty);
                }

                lines.Add(
                    $"{BridgeName} : 1");
            }

            string? modsDirectory =
                Path.GetDirectoryName(modsTextPath);

            if (!string.IsNullOrWhiteSpace(modsDirectory))
            {
                Directory.CreateDirectory(
                    modsDirectory);
            }

            string temporaryPath =
                modsTextPath + ".limelight.tmp";

            try
            {
                File.WriteAllLines(
                    temporaryPath,
                    lines);

                if (File.Exists(modsTextPath))
                {
                    // Keep one small safety copy because mods.txt may also
                    // contain entries belonging to other tools.
                    File.Copy(
                        modsTextPath,
                        modsTextPath + ".limelight.bak",
                        overwrite: true);
                }

                File.Move(
                    temporaryPath,
                    modsTextPath,
                    overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static bool IsBridgeLine(
            string line)
        {
            string[] parts =
                line.Split(
                    ':',
                    count: 2);

            return parts.Length > 0 &&
                   string.Equals(
                       parts[0].Trim(),
                       BridgeName,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEnabledBridgeLine(
            string line)
        {
            string[] parts =
                line.Split(
                    ':',
                    count: 2);

            return parts.Length == 2 &&
                   string.Equals(
                       parts[0].Trim(),
                       BridgeName,
                       StringComparison.OrdinalIgnoreCase) &&
                   parts[1].Trim().StartsWith(
                       "1",
                       StringComparison.Ordinal);
        }
    }
}