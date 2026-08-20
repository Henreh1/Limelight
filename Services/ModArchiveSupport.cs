using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.IO;

namespace Limelight.Services
{
    public static class ModArchiveSupport
    {
        public static IReadOnlyList<string> SupportedExtensions { get; } =
            Array.AsReadOnly(
                new[]
                {
                    ".zip",
                    ".rar",
                    ".7z"
                });

        private static readonly HashSet<string> ReservedWindowsNames =
            new HashSet<string>(
                new[]
                {
                    "CON",
                    "PRN",
                    "AUX",
                    "NUL",
                    "COM1",
                    "COM2",
                    "COM3",
                    "COM4",
                    "COM5",
                    "COM6",
                    "COM7",
                    "COM8",
                    "COM9",
                    "LPT1",
                    "LPT2",
                    "LPT3",
                    "LPT4",
                    "LPT5",
                    "LPT6",
                    "LPT7",
                    "LPT8",
                    "LPT9"
                },
                StringComparer.OrdinalIgnoreCase);

        public static bool IsSupportedArchive(
            string? path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   SupportedExtensions.Contains(
                       Path.GetExtension(path),
                       StringComparer.OrdinalIgnoreCase);
        }

        public static IArchive OpenArchive(
            string archivePath)
        {
            return ArchiveFactory.OpenArchive(
                new FileInfo(archivePath),
                ReaderOptions.ForFilePath);
        }

        public static string EntryPath(
            IEntry entry)
        {
            return (entry.Key ?? string.Empty).Trim();
        }

        public static bool IsRootMarker(
            string entryPath)
        {
            return string.IsNullOrWhiteSpace(entryPath) ||
                   entryPath.Equals(
                       ".",
                       StringComparison.Ordinal) ||
                   entryPath.Equals(
                       "./",
                       StringComparison.Ordinal) ||
                   entryPath.Equals(
                       @".\",
                       StringComparison.Ordinal);
        }

        public static bool ContainsUnsafePath(
            string archivePath)
        {
            string normalisedPath =
                archivePath.Replace('\\', '/');

            if (normalisedPath.StartsWith(
                    "/",
                    StringComparison.Ordinal) ||
                Path.IsPathRooted(archivePath))
            {
                return true;
            }

            foreach (string part in normalisedPath.Split('/'))
            {
                if (part == ".." ||
                    part.IndexOfAny(
                        Path.GetInvalidFileNameChars()) >= 0)
                {
                    return true;
                }

                string windowsName =
                    part
                        .TrimEnd(' ', '.')
                        .Split('.')[0];

                if (ReservedWindowsNames.Contains(windowsName))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ContainsLink(
            IEntry entry)
        {
            return !string.IsNullOrWhiteSpace(
                entry.LinkTarget);
        }
    }
}
