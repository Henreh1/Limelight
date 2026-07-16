using System;
using System.Globalization;

namespace Limelight.Models
{
    public sealed class NexusModFile
    {
        public long ModId { get; init; }

        public int FileId { get; init; }

        public int CategoryId { get; init; }

        public string CategoryName { get; init; } =
            "FILE";

        public string FileName { get; init; } =
            "Unnamed file";

        public string ArchiveName { get; init; } =
            string.Empty;

        public string Description { get; init; } =
            "No description was provided for this file.";

        public string Version { get; init; } =
            string.Empty;

        public long SizeKilobytes { get; init; }

        public long UploadedTimestamp { get; init; }

        public bool IsPrimary { get; init; }

        public int DisplayPriority =>
            CategoryId switch
            {
                1 => 0,
                2 => 1,
                3 => 2,
                4 => 3,
                _ => 4
            };

        public string VersionLabel =>
            string.IsNullOrWhiteSpace(Version)
                ? "VERSION UNKNOWN"
                : $"VERSION {Version}";

        public string SizeLabel =>
            FormatSize(SizeKilobytes * 1024L);

        public string UploadedLabel
        {
            get
            {
                if (UploadedTimestamp <= 0)
                {
                    return "UPLOAD DATE UNKNOWN";
                }

                DateTimeOffset uploadedDate =
                    DateTimeOffset.FromUnixTimeSeconds(
                        UploadedTimestamp);

                return uploadedDate
                    .ToLocalTime()
                    .ToString(
                        "'UPLOADED' dd MMM yyyy",
                        CultureInfo.InvariantCulture)
                    .ToUpperInvariant();
            }
        }

        private static string FormatSize(
            long bytes)
        {
            // Nexus reports file sizes in kilobytes, but showing the most
            // natural unit makes comparing downloads much easier.
            if (bytes >= 1024L * 1024L * 1024L)
            {
                return $"{bytes / (1024d * 1024d * 1024d):0.##} GB";
            }

            if (bytes >= 1024L * 1024L)
            {
                return $"{bytes / (1024d * 1024d):0.##} MB";
            }

            if (bytes >= 1024L)
            {
                return $"{bytes / 1024d:0.##} KB";
            }

            return $"{bytes} B";
        }
    }
}
