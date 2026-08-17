using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Limelight.Services
{
    public sealed class LiveLoaderCommandResult
    {
        public bool Success { get; init; }

        public string Message { get; init; } =
            string.Empty;
    }

    public sealed class LiveLoaderCommandService
    {
        private readonly SemaphoreSlim _luaCommandLock =
            new(1, 1);

        private readonly SemaphoreSlim _nativeCommandLock =
            new(1, 1);

        private string RuntimeDirectory =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Limelight",
                "Runtime");

        public Task<LiveLoaderCommandResult> PingAsync(

            CancellationToken cancellationToken = default)
        {
            return SendAsync(
                "ping",
                cancellationToken);
        }
        public Task<LiveLoaderCommandResult> ScanCharlieAsync(
    CancellationToken cancellationToken = default)
        {
            return SendAsync(
                "scan_charlie",
                cancellationToken);
        }

        public Task<LiveLoaderCommandResult> ReapplyCharlieAsync(
    CancellationToken cancellationToken = default)
        {
            return SendAsync(
                "reapply_charlie",
                cancellationToken);
        }

        public Task<LiveLoaderCommandResult> ActivateCharacterSlotAsync(
            string definitionObjectPath,
            string meshObjectPath,
            string characterName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(definitionObjectPath) ||
                string.IsNullOrWhiteSpace(meshObjectPath) ||
                string.IsNullOrWhiteSpace(characterName) ||
                !definitionObjectPath.StartsWith(
                    "/Game/",
                    StringComparison.Ordinal) ||
                !meshObjectPath.StartsWith(
                    "/Game/",
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A valid Character Slot PPCD, mesh, and character name are required.",
                    nameof(definitionObjectPath));
            }

            return SendAsync(
                "activate_character_slot",
                "command.txt",
                "response.txt",
                new Dictionary<string, string>
                {
                    ["definitionPath"] = definitionObjectPath,
                    ["meshPath"] = meshObjectPath,
                    ["characterName"] = characterName
                },
                TimeSpan.FromSeconds(15),
                cancellationToken);
        }

        public Task<LiveLoaderCommandResult> RegisterMountedAssetsAsync(
            IEnumerable<string> objectPaths,
            CancellationToken cancellationToken = default)
        {
            string joinedPaths =
                string.Join(
                    "|",
                    objectPaths.Distinct(
                        StringComparer.OrdinalIgnoreCase));

            return SendAsync(
                "register_mounted_assets",
                "native-command.txt",
                "native-response.txt",
                new Dictionary<string, string>
                {
                    ["objectPaths"] = joinedPaths
                },
                TimeSpan.FromSeconds(45),
                cancellationToken);
        }

        public Task<LiveLoaderCommandResult> ReleaseRegisteredAssetsAsync(
            CancellationToken cancellationToken = default)
        {
            return SendAsync(
                "release_registered_assets",
                "native-command.txt",
                "native-response.txt",
                arguments: null,
                timeout: TimeSpan.FromSeconds(15),
                cancellationToken);
        }

        public Task<LiveLoaderCommandResult> RememberActiveAssetsAsync(
            IEnumerable<string> objectPaths,
            CancellationToken cancellationToken = default)
        {
            string joinedPaths =
                string.Join(
                    "|",
                    objectPaths.Distinct(
                        StringComparer.OrdinalIgnoreCase));

            return SendAsync(
                "remember_active_assets",
                "command.txt",
                "response.txt",
                new Dictionary<string, string>
                {
                    ["objectPaths"] = joinedPaths
                },
                TimeSpan.FromSeconds(10),
                cancellationToken);
        }

        public Task<LiveLoaderCommandResult> ReloadAssetsAsync(
            IEnumerable<string> objectPaths,
            CancellationToken cancellationToken = default)
        {
            return ReloadAssetsAsync(
                objectPaths,
                requireEveryAsset: false,
                cancellationToken);
        }

        public Task<LiveLoaderCommandResult> VerifyAssetsAsync(
            IEnumerable<string> objectPaths,
            CancellationToken cancellationToken = default)
        {
            // The early preload is allowed to miss dependencies that only
            // become visible after SK_Charlie opens. This final pass is not.
            return ReloadAssetsAsync(
                objectPaths,
                requireEveryAsset: true,
                cancellationToken);
        }

        private Task<LiveLoaderCommandResult> ReloadAssetsAsync(
            IEnumerable<string> objectPaths,
            bool requireEveryAsset,
            CancellationToken cancellationToken)
        {
            string joinedPaths =
                string.Join(
                    "|",
                    objectPaths.Distinct(
                        StringComparer.OrdinalIgnoreCase));

            return SendAsync(
                "reload_assets",
                "command.txt",
                "response.txt",
                new Dictionary<string, string>
                {
                    ["objectPaths"] = joinedPaths,
                    ["requireEveryAsset"] =
                        requireEveryAsset
                            ? "true"
                            : "false"
                },
                TimeSpan.FromSeconds(45),
                cancellationToken);
        }

        public Task<LiveLoaderCommandResult> ConfirmPackageRetirementAsync(
            CancellationToken cancellationToken = default)
        {
            // The native bridge may only retire the old render resources once
            // Lua has proved that the new player mesh and materials are live.
            return SendAsync(
                "confirm_package_retirement",
                "native-command.txt",
                "native-response.txt",
                arguments: null,
                timeout: TimeSpan.FromSeconds(10),
                cancellationToken);
        }

        public Task<LiveLoaderCommandResult> ScanMountFunctionsAsync(
            CancellationToken cancellationToken = default)
        {
            // I warm the native resolver before the first mount. Its result is
            // cached by the bridge, which keeps the expensive scan away from
            // Unreal's game thread when the active mod is finally mounted.
            return SendAsync(
                "resolve_mount",
                "native-command.txt",
                "native-response.txt",
                arguments: null,
                timeout: TimeSpan.FromMinutes(3),
                cancellationToken);
        }

        public Task<LiveLoaderCommandResult> PingNativeAsync(
            CancellationToken cancellationToken = default)
        {
            return SendAsync(
                "ping",
                "native-command.txt",
                "native-response.txt",
                arguments: null,
                timeout: TimeSpan.FromSeconds(15),
                cancellationToken);
        }

        public Task<LiveLoaderCommandResult> CanSwitchModsAsync(
            CancellationToken cancellationToken = default)
        {
            // The native bridge sees Unreal's LoadMap callbacks directly, so
            // it is the best place to decide whether a live change is safe.
            return SendAsync(
                "can_switch_mods",
                "native-command.txt",
                "native-response.txt",
                arguments: null,
                timeout: TimeSpan.FromSeconds(10),
                cancellationToken);
        }

        public Task<LiveLoaderCommandResult> IsWorldStableAsync(
            CancellationToken cancellationToken = default)
        {
            // I use this narrower check between switch stages. Retirement and
            // cooldown checks belong at the start, but a map change must stop
            // every later Unreal mutation as well.
            return SendAsync(
                "is_world_stable",
                "native-command.txt",
                "native-response.txt",
                arguments: null,
                timeout: TimeSpan.FromSeconds(10),
                cancellationToken);
        }

        public Task<LiveLoaderCommandResult> MountPakAsync(
            string pakPath,
            int mountOrder,
            CancellationToken cancellationToken = default)
        {
            return SendAsync(
                "mount_pak",
                "native-command.txt",
                "native-response.txt",
                new Dictionary<string, string>
                {
                    ["pakPath"] = pakPath,
                    ["mountOrder"] = mountOrder.ToString()
                },
                TimeSpan.FromMinutes(3),
                cancellationToken);
        }

        public Task<LiveLoaderCommandResult> UnmountPakAsync(
            string pakPath,
            CancellationToken cancellationToken = default)
        {
            return SendAsync(
                "unmount_pak",
                "native-command.txt",
                "native-response.txt",
                new Dictionary<string, string>
                {
                    ["pakPath"] = pakPath
                },
                TimeSpan.FromSeconds(30),
                cancellationToken);
        }

        public Task<LiveLoaderCommandResult> ReleasePackagesAsync(
            IEnumerable<string> packagePaths,
            CancellationToken cancellationToken = default)
        {
            string joinedPaths =
                string.Join(
                    "|",
                    packagePaths.Distinct(
                        StringComparer.OrdinalIgnoreCase));

            return SendAsync(
                "release_packages",
                "native-command.txt",
                "native-response.txt",
                new Dictionary<string, string>
                {
                    ["packagePaths"] = joinedPaths
                },
                TimeSpan.FromSeconds(30),
                cancellationToken);
        }

        private Task<LiveLoaderCommandResult> SendAsync(
            string action,
            CancellationToken cancellationToken)
        {
            return SendAsync(
                action,
                "command.txt",
                "response.txt",
                arguments: null,
                timeout: TimeSpan.FromSeconds(15),
                cancellationToken);
        }

        private async Task<LiveLoaderCommandResult> SendAsync(
            string action,
            string commandFileName,
            string responseFileName,
            IReadOnlyDictionary<string, string>? arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            SemaphoreSlim commandLock =
                responseFileName.Equals(
                    "native-response.txt",
                    StringComparison.OrdinalIgnoreCase)
                    ? _nativeCommandLock
                    : _luaCommandLock;

            await commandLock.WaitAsync(
                cancellationToken);

            try
            {
                return await SendLockedAsync(
                    action,
                    commandFileName,
                    responseFileName,
                    arguments,
                    timeout,
                    cancellationToken);
            }
            finally
            {
                commandLock.Release();
            }
        }

        private async Task<LiveLoaderCommandResult> SendLockedAsync(
            string action,
            string commandFileName,
            string responseFileName,
            IReadOnlyDictionary<string, string>? arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(
                RuntimeDirectory);

            string commandPath =
                Path.Combine(
                    RuntimeDirectory,
                    commandFileName);

            string temporaryCommandPath =
                commandPath + ".tmp";

            string responsePath =
                Path.Combine(
                    RuntimeDirectory,
                    responseFileName);

            string requestId =
                Guid.NewGuid().ToString("N");

            string commandText =
                $"requestId={requestId}{Environment.NewLine}" +
                $"action={action}{Environment.NewLine}";

            if (arguments != null)
            {
                foreach ((string key, string value) in
                         arguments)
                {
                    if (value.Contains('\r') ||
                        value.Contains('\n'))
                    {
                        throw new InvalidDataException(
                            "A live-loader command contained an invalid line break.");
                    }

                    commandText +=
                        $"{key}={value}{Environment.NewLine}";
                }
            }

            // Write to a temporary file first so the Lua bridge never reads a
            // command while Limelight is still writing it.
            await File.WriteAllTextAsync(
                temporaryCommandPath,
                commandText,
                cancellationToken);

            File.Move(
                temporaryCommandPath,
                commandPath,
                overwrite: true);

            DateTime timeoutAt =
                DateTime.UtcNow.Add(timeout);

            while (DateTime.UtcNow < timeoutAt)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Dictionary<string, string>? response =
                    await TryReadResponseAsync(
                        responsePath,
                        cancellationToken);

                if (response is not null &&
                    response.TryGetValue(
                        "requestId",
                        out string? responseId) &&
                    string.Equals(
                        responseId,
                        requestId,
                        StringComparison.Ordinal))
                {
                    TryDeleteFile(responsePath);

                    bool succeeded =
                        response.TryGetValue(
                            "success",
                            out string? successValue) &&
                        bool.TryParse(
                            successValue,
                            out bool parsedSuccess) &&
                        parsedSuccess;

                    response.TryGetValue(
                        "message",
                        out string? message);

                    return new LiveLoaderCommandResult
                    {
                        Success = succeeded,
                        Message = message ??
                                  "The bridge returned no message."
                    };
                }

                await Task.Delay(
                    100,
                    cancellationToken);
            }

            return new LiveLoaderCommandResult
            {
                Success = false,
                Message = "The live-loader bridge did not respond."
            };
        }

        private static async Task<Dictionary<string, string>?>
            TryReadResponseAsync(
                string responsePath,
                CancellationToken cancellationToken)
        {
            if (!File.Exists(responsePath))
            {
                return null;
            }

            try
            {
                string[] lines =
                    await File.ReadAllLinesAsync(
                        responsePath,
                        cancellationToken);

                Dictionary<string, string> values =
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);

                foreach (string line in lines)
                {
                    int equalsIndex =
                        line.IndexOf('=');

                    if (equalsIndex <= 0)
                    {
                        continue;
                    }

                    string key =
                        line[..equalsIndex].Trim();

                    string value =
                        line[(equalsIndex + 1)..].Trim();

                    values[key] = value;
                }

                return values;
            }
            catch (IOException)
            {
                // The Lua bridge may still be replacing the response file.
                // We can safely try again on the next pass.
                return null;
            }
        }

        private static void TryDeleteFile(
            string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // A stale response is harmless because request IDs prevent
                // Limelight from accepting it for a later command.
            }
        }
    }
}
