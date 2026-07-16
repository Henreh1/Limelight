using Limelight.Models;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Limelight.Services
{
    public sealed class NativeBridgeInstallerService
    {
        private const string ManifestResourceName =
            "Limelight.Payloads.NativeBridge.bridge-manifest.json";

        private const string BinaryResourceName =
            "Limelight.Payloads.NativeBridge.LimelightNativeBridge.dll";

        private readonly Assembly _assembly =
            typeof(NativeBridgeInstallerService).Assembly;

        public NativeBridgePayloadManifest Manifest =>
            LoadManifest();

        public NativeBridgePayloadManifest EnsureInstalled(
            Ue4ssDetectionResult installation)
        {
            ValidateInstallation(installation);

            NativeBridgePayloadManifest manifest =
                LoadManifest();

            ValidateManifest(manifest);

            string targetPath =
                GetTargetPath(
                    installation,
                    manifest);

            Directory.CreateDirectory(
                Path.GetDirectoryName(targetPath)!);

            if (!FileMatchesManifest(
                    targetPath,
                    manifest))
            {
                InstallPayload(
                    targetPath,
                    manifest);
            }

            EnableNativeBridge(
                installation.ModsDirectory!,
                manifest.TargetModName);

            if (!FileMatchesManifest(
                    targetPath,
                    manifest))
            {
                throw new InvalidOperationException(
                    "The Limelight native bridge could not be verified after installation.");
            }

            return manifest;
        }

        public bool IsCurrentVersionInstalled(
            Ue4ssDetectionResult installation)
        {
            try
            {
                ValidateInstallation(installation);

                NativeBridgePayloadManifest manifest =
                    LoadManifest();

                ValidateManifest(manifest);

                string targetPath =
                    GetTargetPath(
                        installation,
                        manifest);

                string modsTextPath =
                    Path.Combine(
                        installation.ModsDirectory!,
                        "mods.txt");

                return
                    FileMatchesManifest(
                        targetPath,
                        manifest) &&
                    IsNativeBridgeEnabled(
                        modsTextPath,
                        manifest.TargetModName);
            }
            catch
            {
                return false;
            }
        }

        public string GetInstalledPath(
            Ue4ssDetectionResult installation)
        {
            ValidateInstallation(installation);

            NativeBridgePayloadManifest manifest =
                LoadManifest();

            ValidateManifest(manifest);

            return GetTargetPath(
                installation,
                manifest);
        }

        private NativeBridgePayloadManifest LoadManifest()
        {
            using Stream stream =
                _assembly.GetManifestResourceStream(
                    ManifestResourceName) ??
                throw new InvalidOperationException(
                    "The embedded native bridge manifest could not be found.");

            NativeBridgePayloadManifest? manifest =
                JsonSerializer.Deserialize<NativeBridgePayloadManifest>(
                    stream,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            return manifest ??
                throw new InvalidOperationException(
                    "The embedded native bridge manifest is invalid.");
        }

        private void InstallPayload(
            string targetPath,
            NativeBridgePayloadManifest manifest)
        {
            string temporaryPath =
                targetPath +
                ".limelight-installing";

            try
            {
                using Stream source =
                    _assembly.GetManifestResourceStream(
                        BinaryResourceName) ??
                    throw new InvalidOperationException(
                        "The embedded native bridge DLL could not be found.");

                using (FileStream destination =
                    new(
                        temporaryPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None))
                {
                    source.CopyTo(destination);
                }

                // I verify the embedded copy before it can replace a working bridge.
                if (!FileMatchesManifest(
                        temporaryPath,
                        manifest))
                {
                    throw new InvalidOperationException(
                        "The embedded native bridge DLL failed its integrity check.");
                }

                // I use a temporary file so an interrupted setup cannot leave half a DLL.
                File.Move(
                    temporaryPath,
                    targetPath,
                    overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch
                {
                    // I leave cleanup best-effort because the verified target is what matters.
                }
            }
        }

        private static string GetTargetPath(
            Ue4ssDetectionResult installation,
            NativeBridgePayloadManifest manifest)
        {
            string modDirectory =
                Path.GetFullPath(
                    Path.Combine(
                        installation.ModsDirectory!,
                        manifest.TargetModName));

            string relativePath =
                manifest.TargetRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(relativePath))
            {
                throw new InvalidOperationException(
                    "The native bridge manifest contains an unsafe target path.");
            }

            string targetPath =
                Path.GetFullPath(
                    Path.Combine(
                        modDirectory,
                        relativePath));

            string requiredPrefix =
                modDirectory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            if (!targetPath.StartsWith(
                    requiredPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The native bridge manifest attempted to leave its mod directory.");
            }

            return targetPath;
        }

        private static bool FileMatchesManifest(
            string filePath,
            NativeBridgePayloadManifest manifest)
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            FileInfo file =
                new(filePath);

            if (file.Length !=
                manifest.PayloadSize)
            {
                return false;
            }

            using FileStream stream =
                File.OpenRead(filePath);

            string actualHash =
                Convert.ToHexString(
                    SHA256.HashData(stream));

            return string.Equals(
                actualHash,
                manifest.PayloadSha256,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void EnableNativeBridge(
            string modsDirectory,
            string modName)
        {
            string modsTextPath =
                Path.Combine(
                    modsDirectory,
                    "mods.txt");

            List<string> lines =
                File.Exists(modsTextPath)
                    ? File.ReadAllLines(modsTextPath).ToList()
                    : new List<string>();

            string enabledLine =
                $"{modName} : 1";

            int existingIndex =
                lines.FindIndex(
                    line =>
                        IsNativeBridgeLine(
                            line,
                            modName));

            if (existingIndex >= 0)
            {
                if (string.Equals(
                        lines[existingIndex].Trim(),
                        enabledLine,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                lines[existingIndex] =
                    enabledLine;
            }
            else
            {
                if (lines.Count > 0 &&
                    !string.IsNullOrWhiteSpace(lines[^1]))
                {
                    lines.Add(string.Empty);
                }

                lines.Add(enabledLine);
            }

            // I only change Limelight's own entry and preserve every other UE4SS mod.
            File.WriteAllLines(
                modsTextPath,
                lines);
        }

        private static bool IsNativeBridgeEnabled(
            string modsTextPath,
            string modName)
        {
            if (!File.Exists(modsTextPath))
            {
                return false;
            }

            string expectedLine =
                $"{modName} : 1";

            return File.ReadLines(modsTextPath)
                .Any(
                    line =>
                        string.Equals(
                            line.Trim(),
                            expectedLine,
                            StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsNativeBridgeLine(
            string line,
            string modName)
        {
            string[] parts =
                line.Split(
                    ':',
                    2,
                    StringSplitOptions.TrimEntries);

            return
                parts.Length == 2 &&
                string.Equals(
                    parts[0],
                    modName,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateInstallation(
            Ue4ssDetectionResult installation)
        {
            ArgumentNullException.ThrowIfNull(
                installation);

            if (!installation.IsInstalled)
            {
                throw new InvalidOperationException(
                    "UE4SS must be installed before adding the native bridge.");
            }

            if (string.IsNullOrWhiteSpace(
                    installation.ModsDirectory))
            {
                throw new DirectoryNotFoundException(
                    "The UE4SS Mods directory could not be determined.");
            }
        }

        private static void ValidateManifest(
            NativeBridgePayloadManifest manifest)
        {
            if (manifest.SchemaVersion != 1 ||
                string.IsNullOrWhiteSpace(
                    manifest.BridgeVersion) ||
                string.IsNullOrWhiteSpace(
                    manifest.TargetModName) ||
                string.IsNullOrWhiteSpace(
                    manifest.TargetRelativePath) ||
                manifest.PayloadSize <= 0 ||
                manifest.PayloadSha256.Length != 64 ||
                !manifest.PayloadSha256.All(
                    Uri.IsHexDigit))
            {
                throw new InvalidOperationException(
                    "The embedded native bridge manifest is incomplete.");
            }

            if (!string.Equals(
                    Path.GetFileName(
                        manifest.TargetModName),
                    manifest.TargetModName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The native bridge manifest contains an unsafe mod name.");
            }
        }
    }
}