using System.Globalization;
using System.Text.Json.Serialization;

namespace Limelight.Models
{
    public enum NexusDownloadStatus
    {
        Queued,
        Downloading,
        Installing,
        Completed,
        Failed,
        Interrupted
    }

    public sealed class NexusDownloadRecord
    {
        public string Id { get; set; } =
            string.Empty;

        public long ModId { get; set; }

        public int FileId { get; set; }

        public string ModName { get; set; } =
            string.Empty;

        public string FileName { get; set; } =
            string.Empty;

        public string Version { get; set; } =
            string.Empty;

        public NexusDownloadStatus Status { get; set; }

        public long BytesReceived { get; set; }

        public long? TotalBytes { get; set; }

        public string StatusMessage { get; set; } =
            string.Empty;

        public DateTimeOffset StartedAt { get; set; }

        public DateTimeOffset? CompletedAt { get; set; }

        public string InstalledModId { get; set; } =
            string.Empty;

        [JsonIgnore]
        public bool IsActive =>
            Status is NexusDownloadStatus.Queued or
                NexusDownloadStatus.Downloading or
                NexusDownloadStatus.Installing;

        [JsonIgnore]
        public bool IsIndeterminate =>
            IsActive &&
            TotalBytes is not > 0;

        [JsonIgnore]
        public int ProgressPercentage =>
            Status == NexusDownloadStatus.Installing
                ? 100
                : TotalBytes is > 0
                    ? (int)Math.Clamp(
                        BytesReceived * 100L / TotalBytes.Value,
                        0,
                        100)
                    : 0;

        [JsonIgnore]
        public string StatusLabel =>
            Status switch
            {
                NexusDownloadStatus.Queued => "QUEUED",
                NexusDownloadStatus.Downloading => "DOWNLOADING",
                NexusDownloadStatus.Installing => "INSTALLING",
                NexusDownloadStatus.Completed => "INSTALLED",
                NexusDownloadStatus.Failed => "FAILED",
                NexusDownloadStatus.Interrupted => "INTERRUPTED",
                _ => "UNKNOWN"
            };

        [JsonIgnore]
        public string VersionLabel =>
            string.IsNullOrWhiteSpace(Version)
                ? $"NEXUS MOD {ModId}"
                : $"VERSION {Version}  •  NEXUS MOD {ModId}";

        [JsonIgnore]
        public string ProgressLabel
        {
            get
            {
                if (Status == NexusDownloadStatus.Completed)
                {
                    return "Downloaded, checked, and added to My Mods.";
                }

                if (Status == NexusDownloadStatus.Installing)
                {
                    return "Download complete. Limelight is installing the archive.";
                }

                if (Status is NexusDownloadStatus.Failed or
                    NexusDownloadStatus.Interrupted)
                {
                    return string.IsNullOrWhiteSpace(StatusMessage)
                        ? "The download did not finish."
                        : StatusMessage;
                }

                if (TotalBytes is > 0)
                {
                    return
                        $"{FormatSize(BytesReceived)} of {FormatSize(TotalBytes.Value)}";
                }

                return string.IsNullOrWhiteSpace(StatusMessage)
                    ? "Waiting for Nexus Mods..."
                    : StatusMessage;
            }
        }

        [JsonIgnore]
        public string TimeLabel
        {
            get
            {
                DateTimeOffset date =
                    (CompletedAt ?? StartedAt).ToLocalTime();

                string prefix =
                    Status switch
                    {
                        NexusDownloadStatus.Completed => "INSTALLED",
                        NexusDownloadStatus.Failed => "FAILED",
                        NexusDownloadStatus.Interrupted => "INTERRUPTED",
                        _ => "STARTED"
                    };

                return
                    $"{prefix} {date.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture).ToUpperInvariant()}";
            }
        }

        private static string FormatSize(
            long bytes)
        {
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
