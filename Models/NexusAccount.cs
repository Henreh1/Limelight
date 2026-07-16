namespace Limelight.Models
{
    public sealed class NexusAccount
    {
        public long UserId { get; init; }

        public string Name { get; init; } =
            string.Empty;

        public bool IsPremium { get; init; }

        public bool IsSupporter { get; init; }
    }
}