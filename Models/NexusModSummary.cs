namespace Limelight.Models
{
    public sealed class NexusModSummary
    {
        public long ModId { get; init; }

        public string Name { get; init; } =
            string.Empty;

        public string Summary { get; init; } =
            string.Empty;

        public string Author { get; init; } =
            string.Empty;

        public string Version { get; init; } =
            string.Empty;

        public string CategoryName { get; init; } =
            "MISCELLANEOUS";

        public string PictureUrl { get; init; } =
            string.Empty;

        public int Endorsements { get; init; }

        public int TotalDownloads { get; init; }

        public string EndorsementLabel =>
            $"{Endorsements:N0} ENDORSEMENTS";

        public string DownloadLabel =>
            $"{TotalDownloads:N0} DOWNLOADS";
    }
}