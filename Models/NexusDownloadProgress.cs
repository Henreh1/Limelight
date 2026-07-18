namespace Limelight.Models
{
    public sealed class NexusDownloadProgress
    {
        public long BytesReceived { get; init; }

        public long? TotalBytes { get; init; }

        public int Percentage =>
            TotalBytes is > 0
                ? (int)Math.Clamp(
                    BytesReceived * 100L / TotalBytes.Value,
                    0,
                    100)
                : 0;
    }
}
