using System.IO;
using System.IO.Compression;

namespace Limelight.Services
{
    public sealed class ModArchiveValidationResult
    {
        public bool IsValid { get; init; }
        public string Message { get; init; } = string.Empty;
        public int PackageFileCount { get; init; }
    }

    public sealed class ModArchiveValidator
    {
        private static readonly string[] SupportedExtensions =
        {
            ".pak",
            ".utoc",
            ".ucas",
            ".sig"
        };

        public ModArchiveValidationResult Validate(string archivePath)
        {
            if (!File.Exists(archivePath))
            {
                return Invalid("The selected archive could not be found.");
            }

            if (!string.Equals(
                    Path.GetExtension(archivePath),
                    ".zip",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Invalid(
                    "Limelight currently accepts ZIP archives only.");
            }

            try
            {
                using ZipArchive archive =
                    ZipFile.OpenRead(archivePath);

                // Inspect the archive before extracting anything to the computer.
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (ContainsUnsafePath(entry.FullName))
                    {
                        return Invalid(
                            "This archive contains an unsafe file path and will not be imported.");
                    }
                }

                List<ZipArchiveEntry> packageFiles =
                    archive.Entries
                        .Where(entry =>
                            SupportedExtensions.Contains(
                                Path.GetExtension(entry.Name),
                                StringComparer.OrdinalIgnoreCase))
                        .ToList();

                bool containsPak = packageFiles.Any(entry =>
                    string.Equals(
                        Path.GetExtension(entry.Name),
                        ".pak",
                        StringComparison.OrdinalIgnoreCase));

                if (!containsPak)
                {
                    return Invalid(
                        "No Unreal Engine .pak file was found in this archive.");
                }

                List<ZipArchiveEntry> utocFiles =
                    packageFiles
                        .Where(entry =>
                            Path.GetExtension(entry.Name).Equals(
                                ".utoc",
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                List<ZipArchiveEntry> ucasFiles =
                    packageFiles
                        .Where(entry =>
                            Path.GetExtension(entry.Name).Equals(
                                ".ucas",
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                // IoStore packages need both files. One without the other is incomplete.
                if (utocFiles.Count != ucasFiles.Count)
                {
                    return Invalid(
                        "This mod is incomplete. Its .utoc and .ucas files must be supplied together.");
                }

                return new ModArchiveValidationResult
                {
                    IsValid = true,
                    PackageFileCount = packageFiles.Count,
                    Message =
                        $"Valid mod archive WEIII. Found {packageFiles.Count} Unreal package files."
                };
            }
            catch (InvalidDataException)
            {
                return Invalid(
                    "The selected file is damaged or is not a valid ZIP archive.");
            }
            catch (IOException exception)
            {
                return Invalid(
                    $"Limelight could not read this archive.\n\n{exception.Message}");
            }
        }

        private static bool ContainsUnsafePath(string archivePath)
        {
            string normalisedPath =
                archivePath.Replace('\\', '/');

            // Parent-directory paths could otherwise extract outside our mod library.
            return normalisedPath.StartsWith('/') ||
                   normalisedPath
                       .Split('/')
                       .Any(part => part == "..");
        }

        private static ModArchiveValidationResult Invalid(
            string message)
        {
            return new ModArchiveValidationResult
            {
                IsValid = false,
                Message = message
            };
        }
    }
}