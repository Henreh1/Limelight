using Limelight.Models;
using System.IO;
using System.Text.Json;

namespace Limelight.Services
{
    public sealed class LiveSessionService
    {
        public const int MaximumMountedContainers = 12;

        private static readonly string[] ManagedExtensions =
        {
            ".pak",
            ".utoc",
            ".ucas",
            ".tmp"
        };

        private readonly string _sessionFile;
        private readonly object _sessionFileLock =
            new object();

        public LiveSessionService()
        {
            string limelightDirectory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Limelight");

            _sessionFile =
                Path.Combine(
                    limelightDirectory,
                    "live-session.json");
        }

        public LiveSessionState Load()
        {
            lock (_sessionFileLock)
            {
                if (!File.Exists(_sessionFile))
                {
                    return new LiveSessionState();
                }

                try
                {
                    string json =
                        File.ReadAllText(
                            _sessionFile);

                    return JsonSerializer.Deserialize<LiveSessionState>(json)
                           ?? new LiveSessionState();
                }
                catch (IOException)
                {
                    return new LiveSessionState();
                }
                catch (JsonException)
                {
                    return new LiveSessionState
                    {
                        Status = LiveSessionStatus.Interrupted,
                        LastError = "The previous live-session record could not be read."
                    };
                }
            }
        }

        public LiveSessionState EnsureSession(
            string gameDirectory)
        {
            LiveSessionState current =
                Load();

            bool canContinueCurrentSession =
                string.Equals(
                    current.GameDirectory,
                    gameDirectory,
                    StringComparison.OrdinalIgnoreCase) &&
                current.Status is not LiveSessionStatus.Closed;

            if (canContinueCurrentSession)
            {
                return current;
            }

            var freshSession =
                new LiveSessionState
                {
                    GameDirectory = gameDirectory,
                    Status = LiveSessionStatus.Initialising
                };

            Save(freshSession);
            return freshSession;
        }

        public bool CanStageContainers(
            string gameDirectory,
            int upcomingContainerCount,
            out string message)
        {
            LiveSessionState state =
                Load();

            bool belongsToCurrentGame =
                string.Equals(
                    state.GameDirectory,
                    gameDirectory,
                    StringComparison.OrdinalIgnoreCase);

            int mountedContainers =
                belongsToCurrentGame &&
                state.Status is not LiveSessionStatus.Closed
                    ? state.Mounts.Count
                    : 0;

            if (mountedContainers + upcomingContainerCount >
                MaximumMountedContainers)
            {
                message =
                    "This game session has reached Limelight's safe live-container limit. " +
                    $"It currently has {mountedContainers} of {MaximumMountedContainers} containers mounted. " +
                    "Close and reopen Dead as Disco before switching again.";

                return false;
            }

            message = string.Empty;
            return true;
        }

        public void BeginActivation(
            InstalledMod mod,
            string gameDirectory)
        {
            LiveSessionState state =
                EnsureSession(gameDirectory);

            state.Status = LiveSessionStatus.Switching;
            state.ActivationInProgress = true;
            state.PendingModId = mod.Id;
            state.PendingModName = mod.DisplayName;
            state.LastError = string.Empty;

            Save(state);
        }

        public void RecordStagedContainers(
            InstalledMod mod,
            IEnumerable<string> pakPaths,
            string gameDirectory)
        {
            LiveSessionState state =
                EnsureSession(gameDirectory);

            foreach (string pakPath in pakPaths)
            {
                state.Mounts.Add(
                    new LiveSessionMountRecord
                    {
                        ModId = mod.Id,
                        ModName = mod.DisplayName,
                        PakPath = pakPath
                    });
            }

            Save(state);
        }

        public void RecordMountedContainer(
            string pakPath,
            int mountOrder)
        {
            LiveSessionState state =
                Load();

            LiveSessionMountRecord? record =
                state.Mounts.LastOrDefault(candidate =>
                    string.Equals(
                        candidate.PakPath,
                        pakPath,
                        StringComparison.OrdinalIgnoreCase) &&
                    !candidate.WasMounted);

            if (record is not null)
            {
                record.WasMounted = true;
                record.MountOrder = mountOrder;
                record.MountedAt = DateTimeOffset.UtcNow;
            }

            Save(state);
        }

        public void CompleteActivation(
            InstalledMod mod)
        {
            LiveSessionState state =
                Load();

            state.Status = LiveSessionStatus.Active;
            state.ActivationInProgress = false;
            state.ActiveModId = mod.Id;
            state.ActiveModName = mod.DisplayName;
            state.PendingModId = string.Empty;
            state.PendingModName = string.Empty;
            state.SuccessfulSwitches++;
            state.LastError = string.Empty;

            Save(state);
        }

        public void FailActivation(
            Exception exception)
        {
            LiveSessionState state =
                Load();

            state.Status = LiveSessionStatus.Interrupted;
            state.ActivationInProgress = false;
            state.PendingModId = string.Empty;
            state.PendingModName = string.Empty;
            state.LastError = exception.Message;

            Save(state);
        }

        public LiveSessionRecoveryResult RecoverClosedGame(
            string gameDirectory)
        {
            LiveSessionState state =
                Load();

            bool wasInterrupted =
                state.ActivationInProgress ||
                state.Status == LiveSessionStatus.Switching ||
                state.Status == LiveSessionStatus.Interrupted;

            LiveSessionCleanupResult cleanup =
                CleanupStagingFiles(
                    gameDirectory);

            string message =
                wasInterrupted
                    ? $"Recovered an interrupted live change and removed {cleanup.DeletedFileCount} staged file(s)."
                    : $"Closed the live session and removed {cleanup.DeletedFileCount} staged file(s).";

            if (cleanup.Errors.Count > 0)
            {
                message +=
                    $" {cleanup.Errors.Count} file(s) still need attention.";

                state.LastError =
                    string.Join(
                        "; ",
                        cleanup.Errors);
            }

            state.GameDirectory = gameDirectory;
            state.Status = LiveSessionStatus.Closed;
            state.ActivationInProgress = false;
            state.ActiveModId = string.Empty;
            state.ActiveModName = string.Empty;
            state.PendingModId = string.Empty;
            state.PendingModName = string.Empty;
            state.Mounts.Clear();
            state.LastRecoveryMessage = message;

            Save(state);

            return new LiveSessionRecoveryResult
            {
                RecoveredInterruptedActivation = wasInterrupted,
                Cleanup = cleanup,
                Message = message
            };
        }

        public LiveSessionCleanupResult RepairClosedSession(
            string gameDirectory)
        {
            LiveSessionCleanupResult cleanup =
                CleanupStagingFiles(
                    gameDirectory);

            CleanupRuntimeFiles();

            LiveSessionState state =
                Load();

            state.GameDirectory = gameDirectory;
            state.Status = LiveSessionStatus.Closed;
            state.ActivationInProgress = false;
            state.ActiveModId = string.Empty;
            state.ActiveModName = string.Empty;
            state.PendingModId = string.Empty;
            state.PendingModName = string.Empty;
            state.Mounts.Clear();
            state.LastRecoveryMessage =
                $"Live Loader repair removed {cleanup.DeletedFileCount} staged file(s).";
            state.LastError =
                cleanup.Errors.Count == 0
                    ? string.Empty
                    : string.Join(
                        "; ",
                        cleanup.Errors);

            Save(state);
            return cleanup;
        }

        public LiveSessionCleanupResult GetStagingSnapshot(
            string gameDirectory)
        {
            string stagingDirectory =
                GetStagingDirectory(
                    gameDirectory);

            if (!Directory.Exists(stagingDirectory))
            {
                return new LiveSessionCleanupResult();
            }

            int fileCount = 0;
            long totalBytes = 0;

            foreach (FileInfo file in
                     GetManagedStagingFiles(stagingDirectory))
            {
                fileCount++;
                totalBytes += file.Length;
            }

            return new LiveSessionCleanupResult
            {
                DeletedFileCount = fileCount,
                DeletedBytes = totalBytes
            };
        }

        private LiveSessionCleanupResult CleanupStagingFiles(
            string gameDirectory)
        {
            string stagingDirectory =
                GetStagingDirectory(
                    gameDirectory);

            if (!Directory.Exists(stagingDirectory))
            {
                return new LiveSessionCleanupResult();
            }

            int deletedFileCount = 0;
            long deletedBytes = 0;
            var errors = new List<string>();

            foreach (FileInfo file in
                     GetManagedStagingFiles(stagingDirectory))
            {
                try
                {
                    long length = file.Length;
                    file.Delete();
                    deletedFileCount++;
                    deletedBytes += length;
                }
                catch (Exception exception)
                {
                    errors.Add(
                        $"{file.Name}: {exception.Message}");
                }
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(
                        stagingDirectory).Any())
                {
                    Directory.Delete(stagingDirectory);
                }
            }
            catch
            {
                // Leaving an empty staging folder behind is harmless.
            }

            return new LiveSessionCleanupResult
            {
                DeletedFileCount = deletedFileCount,
                DeletedBytes = deletedBytes,
                Errors = errors
            };
        }

        private static IEnumerable<FileInfo> GetManagedStagingFiles(
            string stagingDirectory)
        {
            return new DirectoryInfo(stagingDirectory)
                .EnumerateFiles(
                    "Limelight_*",
                    SearchOption.TopDirectoryOnly)
                .Where(file =>
                    ManagedExtensions.Contains(
                        file.Extension,
                        StringComparer.OrdinalIgnoreCase));
        }

        private static string GetStagingDirectory(
            string gameDirectory)
        {
            string fullGameDirectory =
                Path.GetFullPath(gameDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

            return Path.Combine(
                fullGameDirectory,
                "Pagoda",
                "Saved",
                "Limelight",
                "LivePaks");
        }

        private static void CleanupRuntimeFiles()
        {
            string runtimeDirectory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Limelight",
                    "Runtime");

            if (!Directory.Exists(runtimeDirectory))
            {
                return;
            }

            string[] managedFiles =
            {
                "command.txt",
                "command.txt.tmp",
                "response.txt",
                "response.txt.tmp",
                "native-command.txt",
                "native-command.txt.tmp",
                "native-response.txt",
                "native-response.txt.tmp",
                "heartbeat.txt",
                "native-heartbeat.txt"
            };

            foreach (string fileName in managedFiles)
            {
                try
                {
                    string path =
                        Path.Combine(
                            runtimeDirectory,
                            fileName);

                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch
                {
                    // A future heartbeat naturally replaces a stale one.
                }
            }
        }

        private void Save(
            LiveSessionState state)
        {
            lock (_sessionFileLock)
            {
                state.UpdatedAt = DateTimeOffset.UtcNow;

                string? directory =
                    Path.GetDirectoryName(
                        _sessionFile);

                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temporaryFile =
                    _sessionFile + ".tmp";

                string json =
                    JsonSerializer.Serialize(
                        state,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                File.WriteAllText(
                    temporaryFile,
                    json);

                File.Move(
                    temporaryFile,
                    _sessionFile,
                    overwrite: true);
            }
        }
    }
}
