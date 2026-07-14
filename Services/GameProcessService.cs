using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Limelight.Services
{
    public sealed class GameProcessService
    {
        private string? _cachedGameDirectory;
        private IReadOnlyList<string> _cachedProcessNames =
            Array.Empty<string>();

        public bool IsGameRunning(string? gameDirectory)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                return false;
            }

            IReadOnlyList<string> processNames =
                GetPossibleProcessNames(gameDirectory);

            foreach (string processName in processNames)
            {
                try
                {
                    Process[] matchingProcesses =
                        Process.GetProcessesByName(processName);

                    if (matchingProcesses.Length > 0)
                    {
                        // Dispose the Process wrappers because Windows owns
                        // the actual game processes, not Limelight.
                        foreach (Process process in matchingProcesses)
                        {
                            process.Dispose();
                        }

                        return true;
                    }
                }
                catch
                {
                    // A process can close while Windows is returning its
                    // information. We can safely try again on the next check.
                }
            }

            return false;
        }

        private IReadOnlyList<string> GetPossibleProcessNames(
            string gameDirectory)
        {
            string normalizedDirectory;

            try
            {
                normalizedDirectory =
                    Path.GetFullPath(gameDirectory);
            }
            catch
            {
                return Array.Empty<string>();
            }

            if (string.Equals(
                normalizedDirectory,
                _cachedGameDirectory,
                StringComparison.OrdinalIgnoreCase))
            {
                return _cachedProcessNames;
            }

            HashSet<string> processNames =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    "Pagoda-Win64-Shipping",
                    "Pagoda",
                    "DeadAsDisco",
                    "Dead as Disco"
                };

            try
            {
                // The root executable may have a different name after a game
                // update, so include any launchers found beside the game folder.
                foreach (string executablePath in Directory.EnumerateFiles(
                    normalizedDirectory,
                    "*.exe",
                    SearchOption.TopDirectoryOnly))
                {
                    string? fileName =
                        Path.GetFileNameWithoutExtension(executablePath);

                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        processNames.Add(fileName);
                    }
                }
            }
            catch
            {
                // The known Pagoda process names above are still enough for
                // the normal Dead as Disco installation layout.
            }

            _cachedGameDirectory = normalizedDirectory;
            _cachedProcessNames =
                new List<string>(processNames);

            return _cachedProcessNames;
        }
    }
}