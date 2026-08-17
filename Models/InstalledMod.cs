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

        public string CustomDisplayName { get; set; } =
            string.Empty;

        public string InstallDirectory { get; set; } =
            string.Empty;

        public List<string> PackageFiles { get; set; } =
            new List<string>();

        public string ContentFingerprint { get; set; } =
            string.Empty;

        public List<ModAssetPackage> AssetPackages { get; set; } =
            new List<ModAssetPackage>();

        public int AssetManifestVersion { get; set; }

        public string CharacterSlotName { get; set; } =
            string.Empty;

        public string CharacterSlotInfoFile { get; set; } =
            string.Empty;

        public string CharacterSlotMeshPackagePath { get; set; } =
            string.Empty;

        public string CharacterSlotDefinitionPackagePath { get; set; } =
            string.Empty;

        public DateTimeOffset InstalledAt { get; set; } =
            DateTimeOffset.Now;

        public long NexusModId { get; set; }

        public int NexusFileId { get; set; }

        [JsonIgnore]
        public string DisplayName =>
            string.IsNullOrWhiteSpace(CustomDisplayName)
                ? CreateDisplayName(Name)
                : CustomDisplayName.Trim();

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

        [JsonIgnore]
        public bool IsCharacterSlotMod =>
            !string.IsNullOrWhiteSpace(CharacterSlotName) &&
            !string.IsNullOrWhiteSpace(CharacterSlotInfoFile) &&
            !string.IsNullOrWhiteSpace(CharacterSlotMeshPackagePath) &&
            !string.IsNullOrWhiteSpace(CharacterSlotDefinitionPackagePath);

        [JsonIgnore]
        public string CharacterSlotMeshObjectPath =>
            string.IsNullOrWhiteSpace(CharacterSlotMeshPackagePath)
                ? string.Empty
                : CharacterSlotMeshPackagePath +
                  "." +
                  CharacterSlotMeshPackagePath[
                      (CharacterSlotMeshPackagePath.LastIndexOf('/') + 1)..];

        [JsonIgnore]
        public string CharacterSlotDefinitionObjectPath =>
            string.IsNullOrWhiteSpace(CharacterSlotDefinitionPackagePath)
                ? string.Empty
                : CharacterSlotDefinitionPackagePath +
                  "." +
                  CharacterSlotDefinitionPackagePath[
                      (CharacterSlotDefinitionPackagePath.LastIndexOf('/') + 1)..];
    }
}
