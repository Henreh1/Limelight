using System.IO;
using SharpCompress.Archives;
using SharpCompress.Common;

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

            if (!ModArchiveSupport.IsSupportedArchive(
                    archivePath))
            {
                return Invalid(
                    "Limelight accepts ZIP, RAR, and 7Z mod archives.");
            }

            try
            {
                using IArchive archive =
                    ModArchiveSupport.OpenArchive(
                        archivePath);

                if (!archive.IsComplete)
                {
                    return Invalid(
                        "This archive is incomplete. Make sure every volume or part is present before importing it.");
                }

                if (archive.IsEncrypted)
                {
                    return Invalid(
                        "Password-protected archives are not supported. Extract the mod and create an unencrypted ZIP, RAR, or 7Z archive.");
                }

                List<IArchiveEntry> entries =
                    archive.Entries.ToList();

                // Inspect the archive before extracting anything to the computer.
                foreach (IArchiveEntry entry in entries)
                {
                    string entryPath =
                        ModArchiveSupport.EntryPath(entry);

                    if (entry.IsEncrypted)
                    {
                        return Invalid(
                            "Password-protected archives are not supported. Extract the mod and create an unencrypted ZIP, RAR, or 7Z archive.");
                    }

                    if (ModArchiveSupport.ContainsLink(entry) ||
                        ModArchiveSupport.ContainsUnsafePath(entryPath))
                    {
                        return Invalid(
                            "This archive contains an unsafe path or link and will not be imported.");
                    }
                }

                List<IArchiveEntry> packageFiles =
                    entries
                        .Where(entry =>
                            !entry.IsDirectory &&
                            SupportedExtensions.Contains(
                                Path.GetExtension(
                                    ModArchiveSupport.EntryPath(entry)),
                                StringComparer.OrdinalIgnoreCase))
                        .ToList();

                bool containsPak = packageFiles.Any(entry =>
                    string.Equals(
                        Path.GetExtension(
                            ModArchiveSupport.EntryPath(entry)),
                        ".pak",
                        StringComparison.OrdinalIgnoreCase));

                if (!containsPak)
                {
                    return Invalid(
                        "No Unreal Engine .pak file was found in this archive.");
                }

                List<IArchiveEntry> utocFiles =
                    packageFiles
                        .Where(entry =>
                            Path.GetExtension(
                                ModArchiveSupport.EntryPath(entry)).Equals(
                                ".utoc",
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

                List<IArchiveEntry> ucasFiles =
                    packageFiles
                        .Where(entry =>
                            Path.GetExtension(
                                ModArchiveSupport.EntryPath(entry)).Equals(
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
            catch (SharpCompress.Common.CryptographicException)
            {
                return Invalid(
                    "Password-protected archives are not supported. Extract the mod and create an unencrypted ZIP, RAR, or 7Z archive.");
            }
            catch (SharpCompressException)
            {
                return Invalid(
                    "The selected file is damaged, incomplete, encrypted, or is not a valid ZIP, RAR, or 7Z archive.");
            }
            catch (UnauthorizedAccessException exception)
            {
                return Invalid(
                    $"Limelight could not read this archive.\n\n{exception.Message}");
            }
            catch (IOException exception)
            {
                return Invalid(
                    $"Limelight could not read this archive.\n\n{exception.Message}");
            }
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
