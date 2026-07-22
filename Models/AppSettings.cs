using System.Collections.Generic;

namespace Limelight.Models
{
    // Add future user preferences here so they can all share one settings file.
    public sealed class AppSettings
    {
        public string GameDirectory { get; set; } =
            string.Empty;

        public string ActiveModId { get; set; } =
            string.Empty;

        // I keep the X19 group separate from the main library so users
        // can choose exactly which characters appear in the rotation.
        public List<string> X19LoaderModIds { get; set; } =
            new List<string>();

        // Sequential keeps the hand-picked order. Shuffle chooses a different
        // selected character each time without immediately repeating one.
        public bool X19ShuffleEnabled { get; set; }

        // C is unlikely to conflict with normal gameplay, but the user
        // can replace it from Limelight's Settings page.
        public string X19HotkeyGesture { get; set; } =
            "C";

        // Discord presence is public, so I wait for the user to opt in
        // before Limelight shares any activity with the desktop client.
        public bool DiscordRichPresenceEnabled { get; set; }

        // The resource overlay is optional, so I leave it hidden
        // until the user chooses to monitor Limelight.
        public bool ResourceOverlayEnabled { get; set; }

        // A version number lets a future Limelight update introduce a new
        // tour without repeatedly showing the same guide on every launch.
        public int CompletedTutorialVersion { get; set; }

        public string PendingDeploymentModId { get; set; } =
            string.Empty;

        public string DismissedLiveLoaderPromptForGameDirectory { get; set; } =
            string.Empty;

        public string ProtectedNexusApiKey { get; set; } =
    string.Empty;

        public string NexusAccountName { get; set; } =
            string.Empty;

        public List<InstalledMod> InstalledMods { get; set; } =
            new List<InstalledMod>();
    }
}
