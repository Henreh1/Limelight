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

        public DateTimeOffset InstalledAt { get; set; } =
            DateTimeOffset.Now;

        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                // Nexus archives often append a mod ID, version, timestamp,
                // and download token to the readable mod name.
                string cleanedName =
                    Name.Replace('_', ' ').Trim();

                cleanedName = Regex.Replace(
                    cleanedName,
                    @"\s+\d+\s+[\d.]+\s+\d{4}-\d{2}-\d{2}T\S+(?:\s+\S+)?$",
                    string.Empty,
                    RegexOptions.IgnoreCase);

                return cleanedName.Trim();
            }
        }
    }
}