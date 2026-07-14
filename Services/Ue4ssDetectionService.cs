using System;
using System.IO;

namespace Limelight.Services
{
    public sealed class Ue4ssDetectionResult
    {
        public bool IsInstalled { get; init; }

        public bool IsPartiallyInstalled { get; init; }

        public string Win64Directory { get; init; } =
            string.Empty;

        public string WorkingDirectory { get; init; } =
            string.Empty;

        public string ModsDirectory { get; init; } =
            string.Empty;

        public string SettingsPath { get; init; } =
            string.Empty;

        public string LogPath { get; init; } =
            string.Empty;
    }

    public sealed class Ue4ssDetectionService
    {
        public Ue4ssDetectionResult Detect(
            string? gameDirectory)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                return new Ue4ssDetectionResult();
            }

            string win64Directory =
                Path.Combine(
                    gameDirectory,
                    "Pagoda",
                    "Binaries",
                    "Win64");

            if (!Directory.Exists(win64Directory))
            {
                return new Ue4ssDetectionResult
                {
                    Win64Directory = win64Directory
                };
            }

            string proxyPath =
                Path.Combine(
                    win64Directory,
                    "dwmapi.dll");

            string modernWorkingDirectory =
                Path.Combine(
                    win64Directory,
                    "ue4ss");

            string legacyWorkingDirectory =
                win64Directory;

            // Recent UE4SS builds keep everything except the proxy inside
            // a dedicated ue4ss folder. Check that layout first.
            Ue4ssDetectionResult modernResult =
                CheckWorkingDirectory(
                    win64Directory,
                    modernWorkingDirectory,
                    proxyPath);

            if (modernResult.IsInstalled)
            {
                return modernResult;
            }

            // Older releases placed UE4SS.dll, its settings and Mods folder
            // directly beside the game's shipping executable.
            Ue4ssDetectionResult legacyResult =
                CheckWorkingDirectory(
                    win64Directory,
                    legacyWorkingDirectory,
                    proxyPath);

            if (legacyResult.IsInstalled)
            {
                return legacyResult;
            }

            bool hasAnyUe4ssFile =
                modernResult.IsPartiallyInstalled ||
                legacyResult.IsPartiallyInstalled ||
                File.Exists(proxyPath);

            return new Ue4ssDetectionResult
            {
                IsPartiallyInstalled = hasAnyUe4ssFile,
                Win64Directory = win64Directory,

                // Prefer the modern location when Limelight eventually offers
                // to complete or repair an installation.
                WorkingDirectory = modernWorkingDirectory,

                ModsDirectory = Path.Combine(
                    modernWorkingDirectory,
                    "Mods"),

                SettingsPath = Path.Combine(
                    modernWorkingDirectory,
                    "UE4SS-settings.ini"),

                LogPath = Path.Combine(
                    modernWorkingDirectory,
                    "UE4SS.log")
            };
        }

        private static Ue4ssDetectionResult CheckWorkingDirectory(
            string win64Directory,
            string workingDirectory,
            string proxyPath)
        {
            string mainDllPath =
                Path.Combine(
                    workingDirectory,
                    "UE4SS.dll");

            string settingsPath =
                Path.Combine(
                    workingDirectory,
                    "UE4SS-settings.ini");

            string modsDirectory =
                Path.Combine(
                    workingDirectory,
                    "Mods");

            string logPath =
                Path.Combine(
                    workingDirectory,
                    "UE4SS.log");

            bool hasProxy =
                File.Exists(proxyPath);

            bool hasMainDll =
                File.Exists(mainDllPath);

            bool hasSettings =
                File.Exists(settingsPath);

            // All three core files are required before Limelight calls the
            // runtime bridge installed. The Mods folder can be created later.
            bool isInstalled =
                hasProxy &&
                hasMainDll &&
                hasSettings;

            bool isPartiallyInstalled =
                !isInstalled &&
                (hasProxy || hasMainDll || hasSettings);

            return new Ue4ssDetectionResult
            {
                IsInstalled = isInstalled,
                IsPartiallyInstalled = isPartiallyInstalled,
                Win64Directory = win64Directory,
                WorkingDirectory = workingDirectory,
                ModsDirectory = modsDirectory,
                SettingsPath = settingsPath,
                LogPath = logPath
            };
        }
    }
}