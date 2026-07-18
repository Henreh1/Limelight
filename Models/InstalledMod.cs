using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Limelight.Models
{
    public sealed class InstalledMod
    {
        public string Id { get; set; } =
            Guid.NewGuid().ToString("N");

        public string Name { get; set; } =
            "Unnamed mod";

        public string InstallDirectory { get; set; } =
            string.Empty;

        public List<string> PackageFiles { get; set; } =
            new List<string>();

        public List<ModAssetPackage> AssetPackages { get; set; } =
            new List<ModAssetPackage>();

        public int AssetManifestVersion { get; set; }

        public DateTimeOffset InstalledAt { get; set; } =
            DateTimeOffset.Now;

        public long NexusModId { get; set; }

        public int NexusFileId { get; set; }

        [JsonIgnore]
        public string DisplayName =>
    CreateDisplayName(Name);

        public static string CreateDisplayName(
            string originalName)
        {
            // Nexus archives often append a mod ID, version, timestamp,
            // and download token to the readable mod name.
            string cleanedName =
                originalName.Replace('_', ' ').Trim();

            cleanedName = Regex.Replace(
                cleanedName,
                @"\s+\d+\s+[\d.]+\s+\d{4}-\d{2}-\d{2}T\S+(?:\s+\S+)?$",
                string.Empty,
                RegexOptions.IgnoreCase);

            // Collapse accidental repeated spaces so name comparisons remain reliable.
            cleanedName = Regex.Replace(
                cleanedName,
                @"\s+",
                " ");

            return cleanedName.Trim();
        }

        [JsonIgnore]
        public bool IsActive { get; set; }
    }
}
