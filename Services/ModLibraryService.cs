using Limelight.Models;
using System.IO;
using System.IO.Compression;

namespace Limelight.Services
{
    public sealed class ModLibraryService
    {
        private static readonly string[] PackageExtensions =
        {
            ".pak",
            ".utoc",
            ".ucas",
            ".sig"
        };

        private readonly string _libraryDirectory;
        private readonly ModArchiveValidator _validator;
        private readonly ModAssetScannerService _assetScanner;

        public ModLibraryService()
        {
            _libraryDirectory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Limelight",
                "Mods");

            _validator = new ModArchiveValidator();
            _assetScanner = new ModAssetScannerService();
        }

        public InstalledMod Import(
            string archivePath,
            long nexusModId = 0,
            int nexusFileId = 0,
            string? displayName = null)
        {
            // Validate again here so this service is safe even when called
            // from somewhere other than the current Import button.
            ModArchiveValidationResult validation =
                _validator.Validate(archivePath);

            if (!validation.IsValid)
            {
                throw new InvalidDataException(
                    validation.Message);
            }

            string modId = Guid.NewGuid().ToString("N");

            string stagingDirectory = Path.Combine(
                _libraryDirectory,
                ".importing-" + modId);

            string finalDirectory = Path.Combine(
                _libraryDirectory,
                modId);

            Directory.CreateDirectory(stagingDirectory);

            try
            {
                ExtractArchiveSafely(
                    archivePath,
                    stagingDirectory);

                List<string> packageFiles =
                    FindPackageFiles(stagingDirectory);

                // I settle the extracted files into their permanent location before
                // CUE4Parse opens the containers and begins reading their indexes.
                MoveDirectoryWithRetry(
                    stagingDirectory,
                    finalDirectory);

                List<ModAssetPackage> assetPackages =
                    _assetScanner.Scan(finalDirectory);

                return new InstalledMod
                {
                    Id = modId,
                    Name = string.IsNullOrWhiteSpace(displayName)
                        ? CreateDisplayName(archivePath)
                        : displayName.Trim(),
                    InstallDirectory = finalDirectory,
                    PackageFiles = packageFiles,
                    AssetPackages = assetPackages,
                    AssetManifestVersion =
                        ModAssetScannerService.CurrentManifestVersion,
                    InstalledAt = DateTimeOffset.Now,
                    NexusModId = nexusModId,
                    NexusFileId = nexusFileId
                };
            }
            catch
            {
                // I make cleanup best-effort so it never hides the original
                // import error that the user actually needs to see.
                TryDeleteDirectory(
                    stagingDirectory);

                TryDeleteDirectory(
                    finalDirectory);

                throw;
            }
        }

        private static void MoveDirectoryWithRetry(
    string sourceDirectory,
    string destinationDirectory)
        {
            const int maximumAttempts = 6;

            for (int attempt = 1;
                 attempt <= maximumAttempts;
                 attempt++)
            {
                try
                {
                    Directory.Move(
                        sourceDirectory,
                        destinationDirectory);

                    return;
                }
                catch (UnauthorizedAccessException)
                    when (attempt < maximumAttempts)
                {
                    // Windows Security may inspect newly extracted package files
                    // for a moment, so I give it time to release the folder.
                    Thread.Sleep(
                        attempt * 250);
                }
                catch (IOException)
                    when (attempt < maximumAttempts)
                {
                    Thread.Sleep(
                        attempt * 250);
                }
            }
        }

        private static void TryDeleteDirectory(
            string directory)
        {
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
                // Cleanup is helpful, but I preserve the original import error.
            }
        }

        public List<ModAssetPackage> ScanAssets(
            InstalledMod mod)
        {
            // Older Limelight libraries predate asset manifests, so they are
            // scanned lazily the first time the live loader needs one.
            return _assetScanner.Scan(
                mod.InstallDirectory);
        }

        private static void ExtractArchiveSafely(
    string archivePath,
    string destinationDirectory)
        {
            using ZipArchive archive =
                ZipFile.OpenRead(archivePath);

            string safeRoot =
                Path.GetFullPath(destinationDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);

            string safeRootPrefix =
                safeRoot +
                Path.DirectorySeparatorChar;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string entryPath =
                    entry.FullName.Trim();

                // Some ZIP tools add "." as an entry for the archive root.
                // I skip it because the destination folder already represents it.
                if (string.IsNullOrWhiteSpace(entryPath) ||
                    entryPath.Equals(
                        ".",
                        StringComparison.Ordinal) ||
                    entryPath.Equals(
                        "./",
                        StringComparison.Ordinal) ||
                    entryPath.Equals(
                        @".\",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string targetPath =
                    Path.GetFullPath(
                        Path.Combine(
                            destinationDirectory,
                            entry.FullName));

                // I keep every extracted file inside Limelight's private library.
                if (!targetPath.StartsWith(
                        safeRootPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "The archive contains an unsafe file path.");
                }

                bool isDirectory =
                    string.IsNullOrWhiteSpace(entry.Name) ||
                    entryPath.EndsWith(
                        "/",
                        StringComparison.Ordinal) ||
                    entryPath.EndsWith(
                        "\\",
                        StringComparison.Ordinal);

                if (isDirectory)
                {
                    Directory.CreateDirectory(
                        targetPath);

                    continue;
                }

                string? targetFolder =
                    Path.GetDirectoryName(
                        targetPath);

                if (targetFolder != null)
                {
                    Directory.CreateDirectory(
                        targetFolder);
                }

                entry.ExtractToFile(
                    targetPath,
                    overwrite: true);
            }
        }

        private static List<string> FindPackageFiles(
            string modDirectory)
        {
            return Directory
                .EnumerateFiles(
                    modDirectory,
                    "*",
                    SearchOption.AllDirectories)
                .Where(file =>
                    PackageExtensions.Contains(
                        Path.GetExtension(file),
                        StringComparer.OrdinalIgnoreCase))
                .Select(file =>
                    Path.GetRelativePath(
                        modDirectory,
                        file))
                .ToList();
        }

        private static string CreateDisplayName(
            string archivePath)
        {
            string filename =
                Path.GetFileNameWithoutExtension(archivePath);

            // Archive names commonly use underscores in place of spaces.
            return filename
                .Replace('_', ' ')
                .Trim();
        }
    }
}
