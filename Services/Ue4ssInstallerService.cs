using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Limelight.Services
{
    public sealed class Ue4ssInstallResult
    {
        public string BackupDirectory { get; init; } =
            string.Empty;

        public bool CreatedBackup =>
            !string.IsNullOrWhiteSpace(BackupDirectory);
    }

    public sealed class Ue4ssInstallerService
    {
        public Task<Ue4ssInstallResult> InstallAsync(
            string gameDirectory,
            string packagePath,
            CancellationToken cancellationToken = default)
        {
            // ZIP extraction and copying are disk-heavy operations, so keep
            // them away from WPF's interface thread.
            return Task.Run(
                () => Install(
                    gameDirectory,
                    packagePath,
                    cancellationToken),
                cancellationToken);
        }

        private static Ue4ssInstallResult Install(
            string gameDirectory,
            string packagePath,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                throw new ArgumentException(
                    "A game directory is required.",
                    nameof(gameDirectory));
            }

            if (!File.Exists(packagePath))
            {
                throw new FileNotFoundException(
                    "The downloaded UE4SS package could not be found.",
                    packagePath);
            }

            string win64Directory =
                Path.Combine(
                    gameDirectory,
                    "Pagoda",
                    "Binaries",
                    "Win64");

            if (!Directory.Exists(win64Directory))
            {
                throw new DirectoryNotFoundException(
                    "The Dead as Disco Win64 folder could not be found.");
            }

            string stagingDirectory =
                Path.Combine(
                    Path.GetTempPath(),
                    "Limelight",
                    "LiveLoader",
                    "Staging",
                    Guid.NewGuid().ToString("N"));

            string backupDirectory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Limelight",
                    "Backups",
                    "LiveLoader",
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
                    "-" +
                    Guid.NewGuid().ToString("N")[..8]);

            List<string> createdFiles =
                new List<string>();

            Dictionary<string, string> replacedFiles =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            try
            {
                Directory.CreateDirectory(
                    stagingDirectory);

                ExtractSafely(
                    packagePath,
                    stagingDirectory,
                    cancellationToken);

                ValidateStagedPackage(
                    stagingDirectory);

                CopyPackageFiles(
                    stagingDirectory,
                    win64Directory,
                    backupDirectory,
                    createdFiles,
                    replacedFiles,
                    cancellationToken);

                ValidateInstalledFiles(
                    win64Directory);

                if (replacedFiles.Count == 0)
                {
                    TryDeleteDirectory(
                        backupDirectory);

                    backupDirectory =
                        string.Empty;
                }

                return new Ue4ssInstallResult
                {
                    BackupDirectory = backupDirectory
                };
            }
            catch
            {
                // If even one required file fails, put every replaced file
                // back and remove files created by this setup attempt.
                RollBackInstallation(
                    createdFiles,
                    replacedFiles);

                throw;
            }
            finally
            {
                TryDeleteDirectory(
                    stagingDirectory);
            }
        }

        private static void ExtractSafely(
            string packagePath,
            string stagingDirectory,
            CancellationToken cancellationToken)
        {
            string stagingRoot =
                Path.GetFullPath(stagingDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            using ZipArchive archive =
                ZipFile.OpenRead(packagePath);

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string normalizedEntryPath =
                    entry.FullName.Replace(
                        '/',
                        Path.DirectorySeparatorChar);

                string destinationPath =
                    Path.GetFullPath(
                        Path.Combine(
                            stagingDirectory,
                            normalizedEntryPath));

                // A ZIP entry must stay inside our temporary staging folder.
                // This prevents a malformed archive writing elsewhere.
                if (!destinationPath.StartsWith(
                        stagingRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "The UE4SS package contains an unsafe file path.");
                }

                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    Directory.CreateDirectory(
                        destinationPath);

                    continue;
                }

                string? destinationDirectory =
                    Path.GetDirectoryName(destinationPath);

                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(
                        destinationDirectory);
                }

                using Stream source =
                    entry.Open();

                using FileStream destination =
                    new FileStream(
                        destinationPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None);

                source.CopyTo(destination);
            }
        }

        private static void ValidateStagedPackage(
            string stagingDirectory)
        {
            bool hasProxy =
                File.Exists(
                    Path.Combine(
                        stagingDirectory,
                        "dwmapi.dll"));

            bool hasModernLayout =
                File.Exists(
                    Path.Combine(
                        stagingDirectory,
                        "ue4ss",
                        "UE4SS.dll")) &&
                File.Exists(
                    Path.Combine(
                        stagingDirectory,
                        "ue4ss",
                        "UE4SS-settings.ini"));

            bool hasLegacyLayout =
                File.Exists(
                    Path.Combine(
                        stagingDirectory,
                        "UE4SS.dll")) &&
                File.Exists(
                    Path.Combine(
                        stagingDirectory,
                        "UE4SS-settings.ini"));

            if (!hasProxy ||
                (!hasModernLayout && !hasLegacyLayout))
            {
                throw new InvalidDataException(
                    "The staged package does not contain a complete UE4SS installation.");
            }
        }

        private static void CopyPackageFiles(
            string stagingDirectory,
            string win64Directory,
            string backupDirectory,
            ICollection<string> createdFiles,
            IDictionary<string, string> replacedFiles,
            CancellationToken cancellationToken)
        {
            foreach (string sourcePath in Directory.EnumerateFiles(
                         stagingDirectory,
                         "*",
                         SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string relativePath =
                    Path.GetRelativePath(
                        stagingDirectory,
                        sourcePath);

                string destinationPath =
                    Path.Combine(
                        win64Directory,
                        relativePath);

                if (File.Exists(destinationPath) &&
                    ShouldPreserveExistingFile(relativePath))
                {
                    // User settings and existing mods belong to the user.
                    // Repairing the loader should never overwrite them.
                    continue;
                }

                string? destinationDirectory =
                    Path.GetDirectoryName(destinationPath);

                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(
                        destinationDirectory);
                }

                if (File.Exists(destinationPath))
                {
                    string backupPath =
                        Path.Combine(
                            backupDirectory,
                            relativePath);

                    string? backupParent =
                        Path.GetDirectoryName(backupPath);

                    if (!string.IsNullOrWhiteSpace(backupParent))
                    {
                        Directory.CreateDirectory(
                            backupParent);
                    }

                    File.Copy(
                        destinationPath,
                        backupPath,
                        overwrite: true);

                    replacedFiles[destinationPath] =
                        backupPath;
                }
                else
                {
                    // Record this before copying so a partially written file
                    // is still removed if the operation fails.
                    createdFiles.Add(
                        destinationPath);
                }

                File.Copy(
                    sourcePath,
                    destinationPath,
                    overwrite: true);
            }
        }

        private static bool ShouldPreserveExistingFile(
            string relativePath)
        {
            string normalized =
                relativePath.Replace('\\', '/');

            if (normalized.EndsWith(
                    "UE4SS-settings.ini",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return normalized.StartsWith(
                       "Mods/",
                       StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(
                       "ue4ss/Mods/",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateInstalledFiles(
            string win64Directory)
        {
            bool hasProxy =
                File.Exists(
                    Path.Combine(
                        win64Directory,
                        "dwmapi.dll"));

            bool hasModernLayout =
                File.Exists(
                    Path.Combine(
                        win64Directory,
                        "ue4ss",
                        "UE4SS.dll")) &&
                File.Exists(
                    Path.Combine(
                        win64Directory,
                        "ue4ss",
                        "UE4SS-settings.ini"));

            bool hasLegacyLayout =
                File.Exists(
                    Path.Combine(
                        win64Directory,
                        "UE4SS.dll")) &&
                File.Exists(
                    Path.Combine(
                        win64Directory,
                        "UE4SS-settings.ini"));

            if (!hasProxy ||
                (!hasModernLayout && !hasLegacyLayout))
            {
                throw new IOException(
                    "UE4SS could not be verified after installation.");
            }
        }

        private static void RollBackInstallation(
            IEnumerable<string> createdFiles,
            IReadOnlyDictionary<string, string> replacedFiles)
        {
            foreach (string createdFile in createdFiles.Reverse())
            {
                try
                {
                    if (File.Exists(createdFile))
                    {
                        File.Delete(createdFile);
                    }
                }
                catch
                {
                    // Keep restoring the remaining files even if Windows has
                    // already locked one failed copy.
                }
            }

            foreach ((string destinationPath, string backupPath)
                     in replacedFiles)
            {
                try
                {
                    if (File.Exists(backupPath))
                    {
                        File.Copy(
                            backupPath,
                            destinationPath,
                            overwrite: true);
                    }
                }
                catch
                {
                    // The backup remains on disk so it can still be restored
                    // manually if Windows blocks the automatic rollback.
                }
            }
        }

        private static void TryDeleteDirectory(
            string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(
                        directory,
                        recursive: true);
                }
            }
            catch
            {
                // Temporary or backup cleanup should not hide the actual
                // installation result from the user.
            }
        }
    }
}