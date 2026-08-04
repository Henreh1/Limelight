using System.Collections.Generic;

namespace Limelight.Models
{
    public sealed class ReleaseNoteItem
    {
        public required string Eyebrow { get; init; }
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required string Accent { get; init; }
    }

    public sealed class ReleaseNotesContent
    {
        public required string Version { get; init; }
        public required string Eyebrow { get; init; }
        public required string Title { get; init; }
        public required string Summary { get; init; }
        public required IReadOnlyList<ReleaseNoteItem> Items { get; init; }

        // I keep the current release copy together so future updates only
        // need one small file changed before a new Early Access build is packaged.
        public static ReleaseNotesContent CreateCurrent(
            string version)
        {
            return new ReleaseNotesContent
            {
                Version = version,
                Eyebrow = "WELCOME TO EARLY ACCESS",
                Title = "WELCOME TO THE LIMELIGHT STAGE",
                Summary = "Thank you for testing with us. This update focuses on making Nexus browsing, direct downloads, and mod management feel seamless in one place.",
                Items = new List<ReleaseNoteItem>
                {
                    new ReleaseNoteItem
                    {
                        Eyebrow = "PROFILES",
                        Title = "BUILD YOUR CAST",
                        Description = "Save reusable character groups, edit them from compact profile cards, and send a complete cast into X19.",
                        Accent = "#35E7FF"
                    },
                    new ReleaseNoteItem
                    {
                        Eyebrow = "NEXUS INTEGRATION",
                        Title = "DIRECTLY TO LIMELIGHT",
                        Description = "Browse Nexus from Limelight, open pages in one flow, and use Mod Manager download to send files straight into the manager.",
                        Accent = "#FF3CAC"
                    },
                    new ReleaseNoteItem
                    {
                        Eyebrow = "GAME FILES",
                        Title = "RETURN TO VANILLA",
                        Description = "Purge All Mods clears the game's mod folder while keeping your Limelight library, profiles, and settings safe.",
                        Accent = "#885CFF"
                    },
                    new ReleaseNoteItem
                    {
                        Eyebrow = "HOW IT WORKS",
                        Title = "HOW NEXUS DOWNLOADS FLOW",
                        Description = "Sign in on the Nexus card, keep your session active, then choose Mod Manager download on supported pages. Limelight queues, installs, and activates the mod automatically.",
                        Accent = "#35E7FF"
                    }
                }
            };
        }
    }
}
