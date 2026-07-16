using Limelight.Models;
using System.IO;

namespace Limelight.Services
{
    public sealed class LiveModStageResult
    {
        public List<string> PakPaths { get; init; } =
            new List<string>();
    }

    public sealed class LiveModStagingService
    {
        private static readonly string[] RequiredExtensions =
        {
            ".pak",
            ".utoc",
            ".ucas"
        };

        public LiveModStageResult Stage(
            InstalledMod mod,
            string gameDirectory)
        {
            string stagingDirectory =
                Path.Combine(
                    gameDirectory,
                    "Pagoda",
                    "Saved",
                    "Limelight",
                    "LivePaks");

            Directory.CreateDirectory(stagingDirectory);

            Dictionary<string, Dictionary<string, string>> containers =
                FindContainers(mod);

            if (containers.Count == 0)
            {
                throw new InvalidDataException(
                    $"{mod.DisplayName} does not contain a complete pak, utoc, and ucas set.");
            }

            string activationId =
                DateTimeOffset.UtcNow
                    .ToUnixTimeMilliseconds()
                    .ToString();

            var stagedPaks =
                new List<string>();

            int containerNumber = 0;

            foreach (Dictionary<string, string> files in
                     containers.Values)
            {
                ++containerNumber;

                string stagedBaseName =
                    $"Limelight_{mod.Id[..Math.Min(8, mod.Id.Length)]}_" +
                    $"{activationId}_{containerNumber:D2}_P";

                foreach (string extension in
                         RequiredExtensions)
                {
                    string destinationPath =
                        Path.Combine(
                            stagingDirectory,
                            stagedBaseName + extension);

                    string temporaryPath =
                        destinationPath + ".tmp";

                    CopyAtomically(
                        files[extension],
                        temporaryPath,
                        destinationPath);

                    if (extension == ".pak")
                    {
                        stagedPaks.Add(destinationPath);
                    }
                }
            }

            return new LiveModStageResult
            {
                PakPaths = stagedPaks
            };
        }

        private static void CopyAtomically(
            string sourcePath,
            string temporaryPath,
            string destinationPath)
        {
            try
            {
                // The native bridge should only ever see a complete container
                // file, even if a large ucas copy is interrupted.
                File.Copy(
                    sourcePath,
                    temporaryPath,
                    overwrite: true);

                File.Move(
                    temporaryPath,
                    destinationPath,
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

        private static Dictionary<string, Dictionary<string, string>>
            FindContainers(InstalledMod mod)
        {
            var groups =
                new Dictionary<string, Dictionary<string, string>>(
                    StringComparer.OrdinalIgnoreCase);

            string safeLibraryRoot =
                Path.GetFullPath(mod.InstallDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            foreach (string relativePath in
                     mod.PackageFiles)
            {
                string extension =
                    Path.GetExtension(relativePath);

                if (!RequiredExtensions.Contains(
                        extension,
                        StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                string sourcePath =
                    Path.GetFullPath(
                        Path.Combine(
                            mod.InstallDirectory,
                            relativePath));

                if (!sourcePath.StartsWith(
                        safeLibraryRoot,
                        StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(sourcePath))
                {
                    throw new InvalidDataException(
                        $"A package file is missing from {mod.DisplayName}.");
                }

                string groupName =
                    Path.Combine(
                        Path.GetDirectoryName(relativePath) ??
                            string.Empty,
                        Path.GetFileNameWithoutExtension(relativePath));

                if (!groups.TryGetValue(
                        groupName,
                        out Dictionary<string, string>? group))
                {
                    group =
                        new Dictionary<string, string>(
                            StringComparer.OrdinalIgnoreCase);

                    groups[groupName] = group;
                }

                group[extension] = sourcePath;
            }

            return groups
                .Where(group =>
                    RequiredExtensions.All(extension =>
                        group.Value.ContainsKey(extension)))
                .ToDictionary(
                    group => group.Key,
                    group => group.Value,
                    StringComparer.OrdinalIgnoreCase);
        }
    }
}
