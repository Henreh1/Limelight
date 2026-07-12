namespace Limelight.Models
{
    public sealed class InstalledMod
    {
        public string Id { get; set; } =
            Guid.NewGuid().ToString("N");

        public string Name { get; set; } = "Unnamed mod";

        public string InstallDirectory { get; set; } =
            string.Empty;

        public List<string> PackageFiles { get; set; } =
            new List<string>();

        public DateTimeOffset InstalledAt { get; set; } =
            DateTimeOffset.Now;
    }
}