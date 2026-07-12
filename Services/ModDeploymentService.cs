using Limelight.Models;
using System.IO;
using System.Text.Json;

namespace Limelight.Services
{
    public sealed class ModDeploymentService
    {
        private const string ManifestFilename =
            ".limelight-deployment.json";

        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions
            {
                WriteIndented = true
            };

        public void Activate(
            InstalledMod mod,
            string gameDirectory)
        {
            string modsDirectory =
                GetGameModsDirectory(gameDirectory);

            Directory.CreateDirectory(modsDirectory);

            List<string> previouslyManagedFiles =
                LoadManifest(modsDirectory);

            List<DeploymentFile> newFiles =
                BuildDeploymentList(
                    mod,
                    modsDirectory);

            EnsureNoManualFileConflicts(
                newFiles,
                previouslyManagedFiles);

            // Copy everything to temporary files first. The currently active
            // mod stays untouched if one of the source files cannot be read.
            foreach (DeploymentFile file in newFiles)
            {
                File.Copy(
                    file.SourcePath,
                    file.StagingPath,
                    overwrite: true);
            }

            var backups =
                new List<BackupFile>();

            var deployedFiles =
                new List<string>();

            try
            {
                // Move Limelight's old files aside instead of deleting them
                // immediately. This lets us restore them if deployment fails.
                foreach (string filename in previouslyManagedFiles)
                {
                    if (Path.GetFileName(filename) != filename)
                    {
                        continue;
                    }

                    string originalPath =
                        Path.Combine(
                            modsDirectory,
                            filename);

                    if (!File.Exists(originalPath))
                    {
                        continue;
                    }

                    string backupPath =
                        originalPath + ".limelight-backup";

                    File.Move(
                        originalPath,
                        backupPath,
                        overwrite: true);

                    backups.Add(
                        new BackupFile(
                            originalPath,
                            backupPath));
                }

                foreach (DeploymentFile file in newFiles)
                {
                    File.Move(
                        file.StagingPath,
                        file.FinalPath,
                        overwrite: true);

                    deployedFiles.Add(
                        file.FinalPath);
                }

                SaveManifest(
                    modsDirectory,
                    newFiles.Select(file =>
                        Path.GetFileName(file.FinalPath)));
            }
            catch
            {
                // Remove any partially deployed new files.
                foreach (string deployedFile in deployedFiles)
                {
                    if (File.Exists(deployedFile))
                    {
                        File.Delete(deployedFile);
                    }
                }

                // Put the previous active mod back exactly as it was.
                RestoreBackups(backups);

                DeleteStagingFiles(newFiles);

                try
                {
                    SaveManifest(
                        modsDirectory,
                        previouslyManagedFiles);
                }
                catch
                {
                    // Preserve the original deployment exception.
                }

                throw;
            }

            // The new deployment is now committed, so old backups are no
            // longer needed. Failure to remove one does not break the mod.
            foreach (BackupFile backup in backups)
            {
                try
                {
                    if (File.Exists(backup.BackupPath))
                    {
                        File.Delete(backup.BackupPath);
                    }
                }
                catch (IOException)
                {
                    // A leftover backup is harmless and is ignored by Unreal.
                }
            }
        }

        public void Deactivate(string gameDirectory)
        {
            string modsDirectory =
                GetGameModsDirectory(gameDirectory);

            if (!Directory.Exists(modsDirectory))
            {
                return;
            }

            List<string> managedFiles =
                LoadManifest(modsDirectory);

            var backups =
                new List<BackupFile>();

            try
            {
                // Move first and delete later so a failed operation can
                // restore the active mod without losing files.
                foreach (string filename in managedFiles)
                {
                    if (Path.GetFileName(filename) != filename)
                    {
                        continue;
                    }

                    string originalPath =
                        Path.Combine(
                            modsDirectory,
                            filename);

                    if (!File.Exists(originalPath))
                    {
                        continue;
                    }

                    string backupPath =
                        originalPath + ".limelight-backup";

                    File.Move(
                        originalPath,
                        backupPath,
                        overwrite: true);

                    backups.Add(
                        new BackupFile(
                            originalPath,
                            backupPath));
                }

                SaveManifest(
                    modsDirectory,
                    Array.Empty<string>());
            }
            catch
            {
                RestoreBackups(backups);
                throw;
            }

            foreach (BackupFile backup in backups)
            {
                if (File.Exists(backup.BackupPath))
                {
                    File.Delete(backup.BackupPath);
                }
            }
        }

        private static List<DeploymentFile> BuildDeploymentList(
            InstalledMod mod,
            string modsDirectory)
        {
            var deploymentFiles =
                new List<DeploymentFile>();

            var usedFilenames =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (string relativePath in mod.PackageFiles)
            {
                string sourcePath = Path.GetFullPath(
                    Path.Combine(
                        mod.InstallDirectory,
                        relativePath));

                string safeLibraryRoot =
                    Path.GetFullPath(mod.InstallDirectory) +
                    Path.DirectorySeparatorChar;

                if (!sourcePath.StartsWith(
                        safeLibraryRoot,
                        StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(sourcePath))
                {
                    throw new InvalidDataException(
                        $"A package file is missing from {mod.DisplayName}.");
                }

                string filename =
                    Path.GetFileName(sourcePath);

                if (!usedFilenames.Add(filename))
                {
                    throw new InvalidDataException(
                        $"{mod.DisplayName} contains duplicate package filenames.");
                }

                string finalPath =
                    Path.Combine(
                        modsDirectory,
                        filename);

                deploymentFiles.Add(
                    new DeploymentFile(
                        sourcePath,
                        finalPath,
                        finalPath + ".limelight-new"));
            }

            return deploymentFiles;
        }

        private static void EnsureNoManualFileConflicts(
            IEnumerable<DeploymentFile> newFiles,
            IEnumerable<string> managedFiles)
        {
            var managedSet =
                new HashSet<string>(
                    managedFiles,
                    StringComparer.OrdinalIgnoreCase);

            foreach (DeploymentFile file in newFiles)
            {
                string filename =
                    Path.GetFileName(file.FinalPath);

                // Limelight will never overwrite a matching file unless its
                // own manifest proves that Limelight deployed it.
                if (File.Exists(file.FinalPath) &&
                    !managedSet.Contains(filename))
                {
                    throw new IOException(
                        $"{filename} already exists in ~mods and is not managed by Limelight.");
                }
            }
        }

        private static string GetGameModsDirectory(
            string gameDirectory)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                throw new InvalidOperationException(
                    "Connect the Dead as Disco installation first.");
            }

            return Path.Combine(
                gameDirectory,
                "Pagoda",
                "Content",
                "Paks",
                "~mods");
        }

        private static List<string> LoadManifest(
            string modsDirectory)
        {
            string manifestPath =
                Path.Combine(
                    modsDirectory,
                    ManifestFilename);

            if (!File.Exists(manifestPath))
            {
                return new List<string>();
            }

            try
            {
                string json =
                    File.ReadAllText(manifestPath);

                return JsonSerializer.Deserialize<List<string>>(json)
                       ?? new List<string>();
            }
            catch (JsonException)
            {
                // A damaged manifest is treated as untrusted. This prevents
                // Limelight from deleting files it cannot prove it owns.
                return new List<string>();
            }
        }

        private static void SaveManifest(
            string modsDirectory,
            IEnumerable<string> filenames)
        {
            string manifestPath =
                Path.Combine(
                    modsDirectory,
                    ManifestFilename);

            string temporaryPath =
                manifestPath + ".tmp";

            string json =
                JsonSerializer.Serialize(
                    filenames.ToList(),
                    JsonOptions);

            File.WriteAllText(
                temporaryPath,
                json);

            File.Move(
                temporaryPath,
                manifestPath,
                overwrite: true);
        }

        private static void RestoreBackups(
            IEnumerable<BackupFile> backups)
        {
            foreach (BackupFile backup in backups.Reverse())
            {
                if (File.Exists(backup.BackupPath))
                {
                    File.Move(
                        backup.BackupPath,
                        backup.OriginalPath,
                        overwrite: true);
                }
            }
        }

        private static void DeleteStagingFiles(
            IEnumerable<DeploymentFile> files)
        {
            foreach (DeploymentFile file in files)
            {
                if (File.Exists(file.StagingPath))
                {
                    File.Delete(file.StagingPath);
                }
            }
        }

        private sealed record DeploymentFile(
            string SourcePath,
            string FinalPath,
            string StagingPath);

        private sealed record BackupFile(
            string OriginalPath,
            string BackupPath);
    }
}