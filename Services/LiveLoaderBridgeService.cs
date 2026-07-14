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

    local commandPath =
        runtimeDirectory .. "\\command.txt"

    local responsePath =
        runtimeDirectory .. "\\response.txt"

    local lastHeartbeatSecond = 0
    local lastRequestId = nil

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

    local function readValues(path)
        local file = io.open(path, "r")

        if file == nil then
            return nil
        end

        local values = {}

        for line in file:lines() do
            local key, value =
                line:match("^([^=]+)=(.*)$")

            if key ~= nil then
                values[key] = value
            end
        end

        file:close()
        return values
    end

    local function writeResponse(
        requestId,
        success,
        message)

        local temporaryPath =
            responsePath .. ".tmp"

        local responseFile =
            io.open(temporaryPath, "w")

        if responseFile == nil then
            return
        end

        responseFile:write(
            "requestId=" .. tostring(requestId) .. "\n")

        responseFile:write(
            "success=" ..
            (success and "true" or "false") ..
            "\n")

        responseFile:write(
            "message=" .. tostring(message) .. "\n")

        responseFile:close()

        os.remove(responsePath)
        os.rename(
            temporaryPath,
            responsePath)
    end

    local function processCommand()
        local command =
            readValues(commandPath)

        if command == nil then
            return
        end

        local requestId =
            command.requestId

        if requestId == nil or
           requestId == "" then

            os.remove(commandPath)
            return
        end

        if requestId == lastRequestId then
            os.remove(commandPath)
            return
        end

        lastRequestId = requestId

        local action =
            string.lower(
                command.action or "")

        if action == "ping" then
            writeResponse(
                requestId,
                true,
                "Limelight bridge is online")
        else
            writeResponse(
                requestId,
                false,
                "Unknown bridge command: " .. action)
        end

        os.remove(commandPath)
    end

    -- Produce a heartbeat immediately so the dashboard can recognise us.
    writeHeartbeat()
    lastHeartbeatSecond = os.time()

    LoopAsync(250, function()
        local currentSecond =
            os.time()

        if currentSecond ~=
           lastHeartbeatSecond then

            writeHeartbeat()
            lastHeartbeatSecond =
                currentSecond
        end

        processCommand()

        -- Returning false keeps the bridge loop running.
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

        public bool HasBridgeFiles(
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

            // The bridge script only exists after the user has accepted
            // setup, so it is safe for Limelight to repair its mods.txt entry.
            return File.Exists(scriptPath);
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