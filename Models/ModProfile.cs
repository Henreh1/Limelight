using System;
using System.Collections.Generic;

namespace Limelight.Models
{
    public sealed class ModProfile
    {
        public string Id { get; set; } =
            Guid.NewGuid().ToString("N");

        public string Name { get; set; } =
            "New profile";

        // I save mod IDs instead of file paths so renaming a mod does not
        // break the user's carefully arranged profile.
        public List<string> ModIds { get; set; } =
            new List<string>();

        public DateTimeOffset CreatedAt { get; set; } =
            DateTimeOffset.Now;

        public DateTimeOffset UpdatedAt { get; set; } =
            DateTimeOffset.Now;
    }
}
