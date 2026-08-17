using System.IO;
using Limelight.Models;

namespace Limelight.Services
{
    public sealed class CharacterSlotLoaderStatus
    {
        public bool IsInstalled { get; init; }

        public string LogicModsDirectory { get; init; } =
            string.Empty;

        public IReadOnlyList<string> MissingFiles { get; init; } =
            Array.Empty<string>();
    }

    public sealed class CharacterSlotLoaderService
    {
        public const string RuntimeCatalogueFilename =
            "character-slot-catalogue.txt";

        public const string RuntimeModeFilename =
            "character-slot-loader-mode.txt";

        private static readonly string[] RequiredFiles =
        {
            "CharacterLoader.pak",
            "CharacterLoader.utoc",
            "CharacterLoader.ucas"
        };

        public CharacterSlotLoaderStatus Inspect(
            string gameDirectory)
        {
            string logicModsDirectory =
                Path.Combine(
                    gameDirectory,
                    "Pagoda",
                    "Content",
                    "Paks",
                    "LogicMods");

            // I leave the official loader in its author's hands. Limelight only
            // checks that all three travelling companions arrived together.
            List<string> missingFiles =
                RequiredFiles
                    .Where(fileName =>
                        !File.Exists(
                            Path.Combine(
                                logicModsDirectory,
                                fileName)))
                    .ToList();

            return new CharacterSlotLoaderStatus
            {
                IsInstalled = missingFiles.Count == 0,
                LogicModsDirectory = logicModsDirectory,
                MissingFiles = missingFiles
            };
        }

        public void EnsureInstalled(
            string gameDirectory)
        {
            CharacterSlotLoaderStatus status =
                Inspect(gameDirectory);

            if (status.IsInstalled)
            {
                return;
            }

            throw new InvalidOperationException(
                "Character Slot mods need the official Character Loader Logic Mod. " +
                "Install CharacterLoader.pak, CharacterLoader.utoc, and " +
                "CharacterLoader.ucas in Pagoda\\Content\\Paks\\LogicMods, " +
                "then restart Dead as Disco. Missing: " +
                string.Join(", ", status.MissingFiles));
        }

        public void SynchronizeRuntimeCatalogue(
            IEnumerable<InstalledMod> characterSlotMods,
            string gameDirectory)
        {
            string runtimeDirectory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "Limelight",
                    "Runtime");

            Directory.CreateDirectory(
                runtimeDirectory);

            List<string> definitionPaths =
                characterSlotMods
                    .Where(mod =>
                        mod.IsCharacterSlotMod &&
                        Directory.Exists(mod.InstallDirectory))
                    .Select(mod =>
                        mod.CharacterSlotDefinitionObjectPath)
                    .Where(path =>
                        !string.IsNullOrWhiteSpace(path))
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path =>
                        path,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

            WriteAllLinesAtomically(
                Path.Combine(
                    runtimeDirectory,
                    RuntimeCatalogueFilename),
                definitionPaths);

            // I step aside when the author's own Lua loader is both present
            // and enabled. One stage manager is charming; two make duplicates.
            WriteAllTextAtomically(
                Path.Combine(
                    runtimeDirectory,
                    RuntimeModeFilename),
                HasEnabledOfficialLuaLoader(gameDirectory)
                    ? "official"
                    : "limelight");
        }

        private static bool HasEnabledOfficialLuaLoader(
            string gameDirectory)
        {
            string win64Directory =
                Path.Combine(
                    gameDirectory,
                    "Pagoda",
                    "Binaries",
                    "Win64");

            string[] candidateModsDirectories =
            {
                Path.Combine(
                    win64Directory,
                    "ue4ss",
                    "Mods"),
                Path.Combine(
                    win64Directory,
                    "Mods")
            };

            return candidateModsDirectories.Any(
                HasEnabledOfficialLuaLoaderInDirectory);
        }

        private static bool HasEnabledOfficialLuaLoaderInDirectory(
            string modsDirectory)
        {
            string modsTextPath =
                Path.Combine(
                    modsDirectory,
                    "mods.txt");

            if (!Directory.Exists(modsDirectory) ||
                !File.Exists(modsTextPath))
            {
                return false;
            }

            HashSet<string> enabledMods =
                File.ReadLines(modsTextPath)
                    .Select(line =>
                        line.Trim())
                    .Where(line =>
                        !line.StartsWith(";", StringComparison.Ordinal) &&
                        !line.StartsWith("#", StringComparison.Ordinal))
                    .Select(line =>
                        line.Split(
                            ':',
                            2,
                            StringSplitOptions.TrimEntries))
                    .Where(parts =>
                        parts.Length == 2 &&
                        parts[1] == "1")
                    .Select(parts =>
                        parts[0])
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase);

            foreach (string modDirectory in
                     Directory.EnumerateDirectories(modsDirectory))
            {
                string modName =
                    Path.GetFileName(modDirectory);

                if (!enabledMods.Contains(modName) ||
                    modName.Equals(
                        "LimelightBridge",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string scriptPath =
                    Path.Combine(
                        modDirectory,
                        "Scripts",
                        "main.lua");

                if (!File.Exists(scriptPath))
                {
                    continue;
                }

                try
                {
                    string script =
                        File.ReadAllText(scriptPath);

                    if (script.Contains(
                            "AddToModDefinitions",
                            StringComparison.Ordinal) &&
                        script.Contains(
                            "CharacterName",
                            StringComparison.Ordinal) &&
                        script.Contains(
                            "35005383",
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                    // I can safely use Limelight's catalogue if Windows is
                    // momentarily hiding an optional third-party script.
                }
            }

            return false;
        }

        private static void WriteAllLinesAtomically(
            string path,
            IEnumerable<string> lines)
        {
            string temporaryPath =
                path + ".tmp";

            File.WriteAllLines(
                temporaryPath,
                lines);

            File.Move(
                temporaryPath,
                path,
                overwrite: true);
        }

        private static void WriteAllTextAtomically(
            string path,
            string text)
        {
            string temporaryPath =
                path + ".tmp";

            File.WriteAllText(
                temporaryPath,
                text);

            File.Move(
                temporaryPath,
                path,
                overwrite: true);
        }
    }
}
