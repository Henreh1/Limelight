using System;

namespace Limelight.Models
{
    public sealed class NexusApiUsageSnapshot
    {
        public int RequestsThisSession { get; init; }

        public int? DailyRemaining { get; init; }

        public int? HourlyRemaining { get; init; }

        public DateTimeOffset? LastRequestUtc { get; init; }

        public string LastRequestKind { get; init; } =
            "NONE";

        public bool HasQuotaInformation =>
            DailyRemaining.HasValue ||
            HourlyRemaining.HasValue;

        // Testing stops early enough to avoid accidentally exhausting
        // the connected account while I am diagnosing Limelight.
        public bool ShouldPauseRequests =>
            DailyRemaining is <= 50 ||
            HourlyRemaining is <= 10;
    }
}