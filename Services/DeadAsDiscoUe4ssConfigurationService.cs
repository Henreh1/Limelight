using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Limelight.Services
{
    public sealed class DeadAsDiscoUe4ssConfigurationService
    {
        private const string FNameConstructorSignature =
            """
            -- UE 5.7 FName::FName(wchar_t const*, EFindName).
            -- Validated against the current Dead as Disco shipping executable.
            function Register()
                return "48 89 5C 24 ?? 57 48 83 EC 30 48 8B D9 48 89 54 24 20 33 C9 41 8B F8 4C 8B D2 44 8B C9 48 85 D2"
            end

            function OnMatchFound(MatchAddress)
                return MatchAddress
            end
            """;

        private const string StaticConstructObjectSignature =
            """
            -- UE 5.6/5.7 StaticConstructObject_Internal entry.
            -- Validated against the current Dead as Disco shipping executable.
            function Register()
                return "4C 8B DC 55 53 41 56 49 8D AB ? ? ? ? 48 81 EC ? ? ? ? 48 8B 05 ? ? ? ? 48 33 C4 48 89 85 ? ? ? ? 8B 41"
            end

            function OnMatchFound(MatchAddress)
                return MatchAddress
            end
            """;

        private const string GNativesSignature =
            """
            -- UE 5.7 GNatives global resolver.
            -- Validated against the current Dead as Disco shipping executable.
            function Register()
                return "48 8D 05 ?? ?? ?? ?? 48 39 05 ?? ?? ?? ?? 48 8D 05 ?? ?? ?? ?? 48 89 05 ?? ?? ?? ?? 74 ?? C7 05 ?? ?? ?? ?? 00 00 00 00 C3"
            end

            function OnMatchFound(MatchAddress)
                local mov_instruction = MatchAddress + 0x15
                local next_instruction = mov_instruction + 7
                local offset = DerefToInt32(mov_instruction + 3)
                return next_instruction + offset
            end
            """;

        private const string VerifiedRuntimeResourceName =
            "Limelight.Payloads.UE4SS.UE4SS.dll";

        private const string VerifiedRuntimeSha256 =
            "3C5523CE1290157672461491AEA786AAF76AF5DE9B7A0D831D9693F1BED1BB56";

        private const string LegacyVerifierPatchSha256 =
            "46DEFDF0628EB21EFC98297853A6DDFFF38341C29E8CAA5EA2F9A60A08AEC02F";

        private readonly object _compatibilityCacheLock =
            new object();

        private string _cachedDllPath =
            string.Empty;

        private long _cachedDllLength = -1;

        private DateTime _cachedDllWriteTimeUtc;

        private bool _cachedCompatibilityResult;

        public bool IsRuntimeCompatible(
            Ue4ssDetectionResult installation)
        {
            if (!installation.IsInstalled ||
                string.IsNullOrWhiteSpace(
                    installation.WorkingDirectory))
            {
                return false;
            }

            string dllPath =
                Path.Combine(
                    installation.WorkingDirectory,
                    "UE4SS.dll");

            if (!File.Exists(dllPath))
            {
                return false;
            }

            FileInfo dll =
                new FileInfo(dllPath);

            lock (_compatibilityCacheLock)
            {
                if (string.Equals(
                        _cachedDllPath,
                        dll.FullName,
                        StringComparison.OrdinalIgnoreCase) &&
                    _cachedDllLength == dll.Length &&
                    _cachedDllWriteTimeUtc == dll.LastWriteTimeUtc)
                {
                    return _cachedCompatibilityResult;
                }

                using FileStream dllStream =
                    File.OpenRead(dll.FullName);

                string actualHash =
                    Convert.ToHexString(
                        SHA256.HashData(dllStream));

                _cachedDllPath = dll.FullName;
                _cachedDllLength = dll.Length;
                _cachedDllWriteTimeUtc =
                    dll.LastWriteTimeUtc;

                _cachedCompatibilityResult =
                    string.Equals(
                        actualHash,
                        Ue4ssReleaseService.CompatibleDllSha256,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        actualHash,
                        VerifiedRuntimeSha256,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        actualHash,
                        LegacyVerifierPatchSha256,
                        StringComparison.OrdinalIgnoreCase);

                return _cachedCompatibilityResult;
            }
        }

        public bool IsConfigured(
            Ue4ssDetectionResult installation)
        {
            if (!IsRuntimeCompatible(installation) ||
                !IsRuntimeStabilized(installation) ||
                string.IsNullOrWhiteSpace(
                    installation.WorkingDirectory) ||
                string.IsNullOrWhiteSpace(
                    installation.SettingsPath))
            {
                return false;
            }

            string signaturesDirectory =
                Path.Combine(
                    installation.WorkingDirectory,
                    "UE4SS_Signatures");

            if (!FileMatches(
                    Path.Combine(
                        signaturesDirectory,
                        "FName_Constructor.lua"),
                    FNameConstructorSignature) ||
                !FileMatches(
                    Path.Combine(
                        signaturesDirectory,
                        "StaticConstructObject.lua"),
                    StaticConstructObjectSignature) ||
                !FileMatches(
                    Path.Combine(
                        signaturesDirectory,
                        "GNatives.lua"),
                    GNativesSignature))
            {
                return false;
            }

            return SettingsMatch(
                installation.SettingsPath);
        }

        public void Apply(
            Ue4ssDetectionResult installation)
        {
            if (!IsRuntimeCompatible(installation))
            {
                throw new InvalidOperationException(
                    "The installed UE4SS runtime is not the Dead as Disco-compatible build.");
            }

            string signaturesDirectory =
                Path.Combine(
                    installation.WorkingDirectory,
                    "UE4SS_Signatures");

            Directory.CreateDirectory(
                signaturesDirectory);

            string fNamePath =
                Path.Combine(
                    signaturesDirectory,
                    "FName_Constructor.lua");

            string staticConstructObjectPath =
                Path.Combine(
                    signaturesDirectory,
                    "StaticConstructObject.lua");

            string gNativesPath =
                Path.Combine(
                    signaturesDirectory,
                    "GNatives.lua");

            string settingsPath =
                installation.SettingsPath;

            string runtimePath =
                Path.Combine(
                    installation.WorkingDirectory,
                    "UE4SS.dll");

            if (!File.Exists(settingsPath))
            {
                throw new FileNotFoundException(
                    "UE4SS-settings.ini could not be found.",
                    settingsPath);
            }

            string[] managedPaths =
            {
                fNamePath,
                staticConstructObjectPath,
                gNativesPath,
                settingsPath,
                runtimePath
            };

            Dictionary<string, byte[]?> originalFiles =
                CaptureFiles(managedPaths);

            try
            {
                InstallVerifiedRuntime(
                    runtimePath);

                // The current game build needs this resolver before UE4SS can
                // finish starting. The pattern has one verified match in the
                // Dead as Disco shipping executable.
                WriteTextIfChanged(
                    fNamePath,
                    FNameConstructorSignature);

                WriteTextIfChanged(
                    staticConstructObjectPath,
                    StaticConstructObjectSignature);

                WriteTextIfChanged(
                    gNativesPath,
                    GNativesSignature);

                ConfigureSettings(settingsPath);

                if (!IsConfigured(installation))
                {
                    throw new IOException(
                        "The Dead as Disco UE4SS configuration could not be verified.");
                }
            }
            catch
            {
                // Put the user's previous settings and signatures back if any
                // part of the configuration step fails.
                RestoreFiles(originalFiles);
                throw;
            }
        }

        private static bool IsRuntimeStabilized(
            Ue4ssDetectionResult installation)
        {
            if (string.IsNullOrWhiteSpace(
                    installation.WorkingDirectory))
            {
                return false;
            }

            string runtimePath =
                Path.Combine(
                    installation.WorkingDirectory,
                    "UE4SS.dll");

            if (!File.Exists(runtimePath))
            {
                return false;
            }

            using FileStream runtimeStream =
                File.OpenRead(runtimePath);

            string actualHash =
                Convert.ToHexString(
                    SHA256.HashData(runtimeStream));

            return string.Equals(
                actualHash,
                VerifiedRuntimeSha256,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void InstallVerifiedRuntime(
            string runtimePath)
        {
            byte[] runtime =
                File.ReadAllBytes(runtimePath);

            string currentHash =
                Convert.ToHexString(
                    SHA256.HashData(runtime));

            if (string.Equals(
                    currentHash,
                    VerifiedRuntimeSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool canUpgrade =
                string.Equals(
                    currentHash,
                    Ue4ssReleaseService.CompatibleDllSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    currentHash,
                    LegacyVerifierPatchSha256,
                    StringComparison.OrdinalIgnoreCase);

            if (!canUpgrade)
            {
                throw new InvalidDataException(
                    "The installed UE4SS runtime is not the verified Dead as Disco build.");
            }

            using Stream? verifiedRuntimeStream =
                typeof(DeadAsDiscoUe4ssConfigurationService)
                    .Assembly
                    .GetManifestResourceStream(
                        VerifiedRuntimeResourceName);

            if (verifiedRuntimeStream is null)
            {
                throw new InvalidDataException(
                    "Limelight's verified UE4SS runtime is missing.");
            }

            using MemoryStream verifiedRuntimeBuffer =
                new MemoryStream();

            verifiedRuntimeStream.CopyTo(
                verifiedRuntimeBuffer);

            byte[] verifiedRuntime =
                verifiedRuntimeBuffer.ToArray();

            string verifiedHash =
                Convert.ToHexString(
                    SHA256.HashData(verifiedRuntime));

            if (!string.Equals(
                    verifiedHash,
                    VerifiedRuntimeSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Limelight's UE4SS runtime did not match its verified digest.");
            }

            // I ship the tiny verifier correction as one known runtime. The
            // game calling the constructor is proof enough; making it sit a
            // second exam is what caused the crash loop in the first place.
            WriteBytesAtomically(
                runtimePath,
                verifiedRuntime);
        }

        private static void WriteBytesAtomically(
            string path,
            byte[] contents)
        {
            string temporaryPath =
                path + ".limelight.tmp";

            try
            {
                File.WriteAllBytes(
                    temporaryPath,
                    contents);

                File.Move(
                    temporaryPath,
                    path,
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

        private static void ConfigureSettings(
            string settingsPath)
        {
            List<string> lines =
                new List<string>(
                    File.ReadAllLines(settingsPath));

            SetIniValue(lines, "General", "UseCache", "1");
            SetIniValue(lines, "General", "InvalidateCacheIfDLLDiffers", "1");
            SetIniValue(lines, "General", "SecondsToScanBeforeGivingUp", "30");
            SetIniValue(lines, "General", "bUseUObjectArrayCache", "false");
            SetIniValue(lines, "General", "DoEarlyScan", "0");

            SetIniValue(lines, "EngineVersionOverride", "MajorVersion", "5");
            SetIniValue(lines, "EngineVersionOverride", "MinorVersion", "7");
            SetIniValue(lines, "EngineVersionOverride", "DebugBuild", "false");

            // Limelight reports loader health itself, so public users do not
            // need UE4SS debug windows opening over the game.
            SetIniValue(lines, "Debug", "ConsoleEnabled", "0");
            SetIniValue(lines, "Debug", "GuiConsoleEnabled", "0");
            SetIniValue(lines, "Debug", "GuiConsoleVisible", "0");
            SetIniValue(lines, "Debug", "GraphicsAPI", "opengl");
            SetIniValue(lines, "Debug", "RenderMode", "ExternalThread");

            SetIniValue(lines, "Hooks", "EngineTickResolveMethod", "Scan");

            string updatedSettings =
                string.Join(
                    Environment.NewLine,
                    lines) +
                Environment.NewLine;

            WriteTextIfChanged(
                settingsPath,
                updatedSettings);
        }

        private static bool SettingsMatch(
            string settingsPath)
        {
            if (!File.Exists(settingsPath))
            {
                return false;
            }

            string[] lines =
                File.ReadAllLines(settingsPath);

            return HasIniValue(lines, "General", "UseCache", "1") &&
                   HasIniValue(lines, "General", "InvalidateCacheIfDLLDiffers", "1") &&
                   HasIniValue(lines, "General", "SecondsToScanBeforeGivingUp", "30") &&
                   HasIniValue(lines, "General", "bUseUObjectArrayCache", "false") &&
                   HasIniValue(lines, "General", "DoEarlyScan", "0") &&
                   HasIniValue(lines, "EngineVersionOverride", "MajorVersion", "5") &&
                   HasIniValue(lines, "EngineVersionOverride", "MinorVersion", "7") &&
                   HasIniValue(lines, "EngineVersionOverride", "DebugBuild", "false") &&
                   HasIniValue(lines, "Debug", "ConsoleEnabled", "0") &&
                   HasIniValue(lines, "Debug", "GuiConsoleEnabled", "0") &&
                   HasIniValue(lines, "Debug", "GuiConsoleVisible", "0") &&
                   HasIniValue(lines, "Debug", "GraphicsAPI", "opengl") &&
                   HasIniValue(lines, "Debug", "RenderMode", "ExternalThread") &&
                   HasIniValue(lines, "Hooks", "EngineTickResolveMethod", "Scan");
        }

        private static void SetIniValue(
            List<string> lines,
            string section,
            string key,
            string value)
        {
            string sectionHeading =
                $"[{section}]";

            int sectionIndex =
                lines.FindIndex(line =>
                    string.Equals(
                        line.Trim(),
                        sectionHeading,
                        StringComparison.OrdinalIgnoreCase));

            if (sectionIndex < 0)
            {
                if (lines.Count > 0 &&
                    !string.IsNullOrWhiteSpace(lines[^1]))
                {
                    lines.Add(string.Empty);
                }

                lines.Add(sectionHeading);
                lines.Add($"{key} = {value}");
                return;
            }

            int nextSectionIndex =
                FindNextSectionIndex(
                    lines,
                    sectionIndex + 1);

            int keyIndex = -1;

            for (int index = sectionIndex + 1;
                 index < nextSectionIndex;
                 index++)
            {
                if (TryReadIniAssignment(
                        lines[index],
                        out string existingKey,
                        out _) &&
                    string.Equals(
                        existingKey,
                        key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    keyIndex = index;
                    break;
                }
            }

            if (keyIndex >= 0)
            {
                lines[keyIndex] =
                    $"{key} = {value}";
            }
            else
            {
                lines.Insert(
                    nextSectionIndex,
                    $"{key} = {value}");
            }
        }

        private static bool HasIniValue(
            IReadOnlyList<string> lines,
            string section,
            string key,
            string expectedValue)
        {
            string sectionHeading =
                $"[{section}]";

            int sectionIndex = -1;

            for (int index = 0;
                 index < lines.Count;
                 index++)
            {
                if (string.Equals(
                        lines[index].Trim(),
                        sectionHeading,
                        StringComparison.OrdinalIgnoreCase))
                {
                    sectionIndex = index;
                    break;
                }
            }

            if (sectionIndex < 0)
            {
                return false;
            }

            int nextSectionIndex =
                FindNextSectionIndex(
                    lines,
                    sectionIndex + 1);

            for (int index = sectionIndex + 1;
                 index < nextSectionIndex;
                 index++)
            {
                if (TryReadIniAssignment(
                        lines[index],
                        out string existingKey,
                        out string existingValue) &&
                    string.Equals(
                        existingKey,
                        key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return string.Equals(
                        existingValue,
                        expectedValue,
                        StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }

        private static int FindNextSectionIndex(
            IReadOnlyList<string> lines,
            int startIndex)
        {
            for (int index = startIndex;
                 index < lines.Count;
                 index++)
            {
                string trimmed =
                    lines[index].Trim();

                if (trimmed.StartsWith(
                        "[",
                        StringComparison.Ordinal) &&
                    trimmed.EndsWith(
                        "]",
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return lines.Count;
        }

        private static bool TryReadIniAssignment(
            string line,
            out string key,
            out string value)
        {
            key = string.Empty;
            value = string.Empty;

            string trimmed =
                line.Trim();

            if (string.IsNullOrWhiteSpace(trimmed) ||
                trimmed.StartsWith(
                    ";",
                    StringComparison.Ordinal) ||
                trimmed.StartsWith(
                    "#",
                    StringComparison.Ordinal))
            {
                return false;
            }

            int equalsIndex =
                trimmed.IndexOf('=');

            if (equalsIndex <= 0)
            {
                return false;
            }

            key =
                trimmed[..equalsIndex].Trim();

            value =
                trimmed[(equalsIndex + 1)..].Trim();

            return true;
        }

        private static bool FileMatches(
            string path,
            string expectedContent)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            string actualContent =
                File.ReadAllText(path);

            return string.Equals(
                NormalizeLineEndings(actualContent).Trim(),
                NormalizeLineEndings(expectedContent).Trim(),
                StringComparison.Ordinal);
        }

        private static string NormalizeLineEndings(
            string value)
        {
            return value
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
        }

        private static Dictionary<string, byte[]?> CaptureFiles(
            IEnumerable<string> paths)
        {
            Dictionary<string, byte[]?> files =
                new Dictionary<string, byte[]?>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (string path in paths)
            {
                files[path] =
                    File.Exists(path)
                        ? File.ReadAllBytes(path)
                        : null;
            }

            return files;
        }

        private static void RestoreFiles(
            IReadOnlyDictionary<string, byte[]?> files)
        {
            foreach ((string path, byte[]? contents) in files)
            {
                try
                {
                    if (contents is null)
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }

                        continue;
                    }

                    File.WriteAllBytes(
                        path,
                        contents);
                }
                catch
                {
                    // Continue restoring the remaining files even if Windows
                    // has already locked one of them.
                }
            }
        }

        private static void WriteTextIfChanged(
            string path,
            string contents)
        {
            if (File.Exists(path) &&
                string.Equals(
                    File.ReadAllText(path),
                    contents,
                    StringComparison.Ordinal))
            {
                return;
            }

            string? directory =
                Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath =
                path + ".limelight.tmp";

            try
            {
                File.WriteAllText(
                    temporaryPath,
                    contents);

                File.Move(
                    temporaryPath,
                    path,
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
    }
}
