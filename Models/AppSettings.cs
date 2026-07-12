namespace Limelight.Models
{
    // Add future user preferences here so they can all share one settings file.
    public sealed class AppSettings
    {
        public string GameDirectory { get; set; } =
            string.Empty;

        public List<InstalledMod> InstalledMods { get; set; } =
            new List<InstalledMod>();
    }
}