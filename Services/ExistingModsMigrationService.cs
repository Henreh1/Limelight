using Limelight.Models;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Limelight.Services
{
    public sealed class ExistingModsMigrationService
    {
        private static readonly string[] PackageExtensions =
        {
            ".pak",
            ".utoc",
            ".ucas",
            ".sig"
        };

        private readonly string _libraryDirectory;

        public ExistingModsMigrationService()
        {
            _libraryDirectory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Limelight",
                "Mods");
        }

        public int CountExistingMods(string gameDirectory)
        {
            return FindExistingModSets(
                gameDirectory).Count;
        }

        public ExistingModsMigrationPlan PrepareMigration(
            string gameDirectory,
            IEnumerable<InstalledMod> libraryMods)
        {
            List<ExistingModSet> existingSets =
                FindExistingModSets(gameDirectory);

            List<InstalledMod> librarySnapshot =
                libraryMods.ToList();

            var importedMods =
                new List<InstalledMod>();

            try
            {
                foreach (ExistingModSet existingSet in existingSets)
                {
                    // If an identical library copy already exists, there
                    // is no need to create another duplicate.
                    bool alreadyInLibrary =
                        librarySnapshot.Any(mod =>
                            PackageSetsMatch(
                                existingSet,
                                mod));

                    if (alreadyInLibrary)
                    {
                        continue;
                    }

                    InstalledMod importedMod =
                        CopyIntoLibrary(
                            existingSet,
                            librarySnapshot);

                    importedMods.Add(importedMod);
                    librarySnapshot.Add(importedMod);
                }

                return new ExistingModsMigrationPlan
                {
                    ImportedMods = importedMods,
                    ExistingSets = existingSets
                };
            }
            catch
            {
                // A failed preparation leaves the original game files alone.
                foreach (InstalledMod importedMod in importedMods)
                {
                    if (Directory.Exists(
                            importedMod.InstallDirectory))
                    {
                        Directory.Delete(
                            importedMod.InstallDirectory,
                            recursive: true);
                    }
                }

                throw;
            }
        }

        public void CompleteMigration(
            ExistingModsMigrationPlan plan)
        {
            // This is called only after settings.json has successfully
            // recorded every new library entry.
            foreach (ExistingModSet existingSet in plan.ExistingSets)
            {
                foreach (string sourceFile in existingSet.Files)
                {
                    if (File.Exists(sourceFile))
                    {
                        File.Delete(sourceFile);
                    }
                }
            }
        }

        private List<ExistingModSet> FindExistingModSets(
            string gameDirectory)
        {
            string modsDirectory = Path.Combine(
                gameDirectory,
                "Pagoda",
                "Content",
                "Paks",
                "~mods");

            if (!Directory.Exists(modsDirectory))
            {
                return new List<ExistingModSet>();
            }

            HashSet<string> limelightManagedFiles =
                LoadManagedFilenames(modsDirectory);

            List<string> packageFiles =
                Directory
                    .EnumerateFiles(
                        modsDirectory,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .Where(path =>
                        PackageExtensions.Contains(
                            Path.GetExtension(path),
                            StringComparer.OrdinalIgnoreCase))
                    .ToList();

            return packageFiles
                .GroupBy(
                    path => Path.GetFileNameWithoutExtension(path),
                    StringComparer.OrdinalIgnoreCase)
                .Where(group =>
                    group.Any(path =>
                        Path.GetExtension(path).Equals(
                            ".pak",
                            StringComparison.OrdinalIgnoreCase)))
                .Where(group =>
                    group.All(path =>
                        !limelightManagedFiles.Contains(
                            Path.GetFileName(path))))
                .Select(group =>
                    new ExistingModSet
                    {
                        DisplayName =
                            CreateDisplayName(group.Key),

                        Files =
                            group.ToList()
                    })
                .ToList();
        }

        private InstalledMod CopyIntoLibrary(
            ExistingModSet existingSet,
            IEnumerable<InstalledMod> libraryMods)
        {
            string modId =
                Guid.NewGuid().ToString("N");

            string stagingDirectory = Path.Combine(
                _libraryDirectory,
                ".migrating-" + modId);

            string finalDirectory = Path.Combine(
                _libraryDirectory,
                modId);

            Directory.CreateDirectory(
                stagingDirectory);

            try
            {
                var packageFilenames =
                    new List<string>();

                foreach (string sourceFile in existingSet.Files)
                {
                    string filename =
                        Path.GetFileName(sourceFile);

                    File.Copy(
                        sourceFile,
                        Path.Combine(
                            stagingDirectory,
                            filename),
                        overwrite: true);

                    packageFilenames.Add(filename);
                }

                string displayName =
                    existingSet.DisplayName;

                // Preserve different variants that happen to share
                // the same readable filename.
                if (libraryMods.Any(mod =>
                        mod.DisplayName.Equals(
                            displayName,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    displayName += " (Migrated)";
                }

                Directory.Move(
                    stagingDirectory,
                    finalDirectory);

                return new InstalledMod
                {
                    Id = modId,
                    Name = displayName,
                    InstallDirectory = finalDirectory,
                    PackageFiles = packageFilenames,
                    InstalledAt = DateTimeOffset.Now
                };
            }
            catch
            {
                if (Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(
                        stagingDirectory,
                        recursive: true);
                }

                throw;
            }
        }

        private static bool PackageSetsMatch(
            ExistingModSet existingSet,
            InstalledMod libraryMod)
        {
            List<string> libraryFiles =
                libraryMod.PackageFiles
                    .Select(relativePath =>
                        Path.Combine(
                            libraryMod.InstallDirectory,
                            relativePath))
                    .ToList();

            return CreatePackageSignature(
                       existingSet.Files) ==
                   CreatePackageSignature(
                       libraryFiles);
        }

        private static string CreatePackageSignature(
            IEnumerable<string> filePaths)
        {
            var parts =
                new List<string>();

            foreach (string filePath in filePaths)
            {
                if (!File.Exists(filePath))
                {
                    return string.Empty;
                }

                var fileInfo =
                    new FileInfo(filePath);

                parts.Add(
                    $"{fileInfo.Name.ToLowerInvariant()}:{fileInfo.Length}");
            }

            return string.Join(
                "|",
                parts.OrderBy(part => part));
        }

        private static string CreateDisplayName(
            string packageBaseName)
        {
            string name =
                packageBaseName.Replace('_', ' ');

            // Unreal mod packages commonly end with _P.
            name = Regex.Replace(
                name,
                @"[ \-_]+P$",
                string.Empty,
                RegexOptions.IgnoreCase);

            return name.Trim();
        }

        private static HashSet<string> LoadManagedFilenames(
            string modsDirectory)
        {
            string manifestPath = Path.Combine(
                modsDirectory,
                ".limelight-deployment.json");

            if (!File.Exists(manifestPath))
            {
                return new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                List<string> filenames =
                    JsonSerializer.Deserialize<List<string>>(
                        File.ReadAllText(manifestPath))
                    ?? new List<string>();

                return new HashSet<string>(
                    filenames,
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exception)
                when (exception is IOException or JsonException)
            {
                // A damaged manifest is not trusted as proof of ownership.
                return new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public sealed class ExistingModsMigrationPlan
    {
        public List<InstalledMod> ImportedMods { get; init; } =
            new List<InstalledMod>();

        internal List<ExistingModSet> ExistingSets { get; init; } =
            new List<ExistingModSet>();
    }

    internal sealed class ExistingModSet
    {
        public string DisplayName { get; init; } =
            string.Empty;

        public List<string> Files { get; init; } =
            new List<string>();
    }
}