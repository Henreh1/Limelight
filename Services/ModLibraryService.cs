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

        public ModLibraryService()
        {
            _libraryDirectory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Limelight",
                "Mods");

            _validator = new ModArchiveValidator();
        }

        public InstalledMod Import(string archivePath)
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

                // Moving the finished folder keeps half-imported mods out
                // of the user's main library.
                Directory.Move(
                    stagingDirectory,
                    finalDirectory);

                return new InstalledMod
                {
                    Id = modId,
                    Name = CreateDisplayName(archivePath),
                    InstallDirectory = finalDirectory,
                    PackageFiles = packageFiles,
                    InstalledAt = DateTimeOffset.Now
                };
            }
            catch
            {
                // Failed imports should not leave temporary files behind.
                if (Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(
                        stagingDirectory,
                        recursive: true);
                }

                throw;
            }
        }

        private static void ExtractArchiveSafely(
            string archivePath,
            string destinationDirectory)
        {
            using ZipArchive archive =
                ZipFile.OpenRead(archivePath);

            string safeRoot =
                Path.GetFullPath(destinationDirectory) +
                Path.DirectorySeparatorChar;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                // Entries with no filename represent folders.
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                string targetPath = Path.GetFullPath(
                    Path.Combine(
                        destinationDirectory,
                        entry.FullName));

                // Never allow a ZIP entry to escape Limelight's library.
                if (!targetPath.StartsWith(
                        safeRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "The archive contains an unsafe file path.");
                }

                string? targetFolder =
                    Path.GetDirectoryName(targetPath);

                if (targetFolder != null)
                {
                    Directory.CreateDirectory(targetFolder);
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