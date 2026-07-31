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
        // need one small file changed before a new Preview is packaged.
        public static ReleaseNotesContent CreateCurrent(
            string version)
        {
            return new ReleaseNotesContent
            {
                Version = version,
                Eyebrow = "WHAT'S NEW IN PREVIEW 3",
                Title = "THE NEXT ACT IS READY",
                Summary = "Build reusable casts, bring mods into the spotlight faster, and keep your game directory under control.",
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
                        Eyebrow = "MOD LIBRARY",
                        Title = "DROP INTO THE SPOTLIGHT",
                        Description = "Drag mod archives onto Limelight. Invalid packages and duplicate content are stopped before they reach your library.",
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
                        Eyebrow = "POLISH",
                        Title = "ONE CONSISTENT STAGE",
                        Description = "Themed prompts, a Limelight file explorer, and clearer compatibility details keep every part of the manager in character.",
                        Accent = "#35E7FF"
                    }
                }
            };
        }
    }
}
