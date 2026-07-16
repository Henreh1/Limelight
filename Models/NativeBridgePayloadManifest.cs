namespace Limelight.Models
{
    public sealed class NativeBridgePayloadManifest
    {
        public int SchemaVersion { get; init; }

        public string BridgeVersion { get; init; } =
            string.Empty;

        public string MinimumLimelightVersion { get; init; } =
            string.Empty;

        public string Ue4ssVersion { get; init; } =
            string.Empty;

        public string Ue4ssCommit { get; init; } =
            string.Empty;

        public string TargetModName { get; init; } =
            string.Empty;

        public string TargetRelativePath { get; init; } =
            string.Empty;

        public string PayloadFileName { get; init; } =
            string.Empty;

        public long PayloadSize { get; init; }

        public string PayloadSha256 { get; init; } =
            string.Empty;
    }
}