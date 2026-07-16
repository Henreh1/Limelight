using System.Text.Json.Serialization;

namespace Limelight.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LiveSessionStatus
    {
        Idle,
        Initialising,
        Switching,
        Active,
        Interrupted,
        Closed
    }

    public sealed class LiveSessionMountRecord
    {
        public string ModId { get; set; } =
            string.Empty;

        public string ModName { get; set; } =
            string.Empty;

        public string PakPath { get; set; } =
            string.Empty;

        public int MountOrder { get; set; }

        public bool WasMounted { get; set; }

        public DateTimeOffset StagedAt { get; set; } =
            DateTimeOffset.UtcNow;

        public DateTimeOffset? MountedAt { get; set; }
    }

    public sealed class LiveSessionState
    {
        public string SessionId { get; set; } =
            Guid.NewGuid().ToString("N");

        public string GameDirectory { get; set; } =
            string.Empty;

        public DateTimeOffset StartedAt { get; set; } =
            DateTimeOffset.UtcNow;

        public DateTimeOffset UpdatedAt { get; set; } =
            DateTimeOffset.UtcNow;

        public LiveSessionStatus Status { get; set; } =
            LiveSessionStatus.Idle;

        public bool ActivationInProgress { get; set; }

        public string ActiveModId { get; set; } =
            string.Empty;

        public string ActiveModName { get; set; } =
            string.Empty;

        public string PendingModId { get; set; } =
            string.Empty;

        public string PendingModName { get; set; } =
            string.Empty;

        public int SuccessfulSwitches { get; set; }

        public string LastError { get; set; } =
            string.Empty;

        public string LastRecoveryMessage { get; set; } =
            string.Empty;

        public List<LiveSessionMountRecord> Mounts { get; set; } =
            new List<LiveSessionMountRecord>();
    }

    public sealed class LiveSessionCleanupResult
    {
        public int DeletedFileCount { get; init; }

        public long DeletedBytes { get; init; }

        public List<string> Errors { get; init; } =
            new List<string>();
    }

    public sealed class LiveSessionRecoveryResult
    {
        public bool RecoveredInterruptedActivation { get; init; }

        public LiveSessionCleanupResult Cleanup { get; init; } =
            new LiveSessionCleanupResult();

        public string Message { get; init; } =
            string.Empty;
    }
}
