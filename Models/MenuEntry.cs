namespace QuickLaunchMenuWinForms.Models
{
    /// <summary>A launchable link (program/folder/document). May live at top level or inside a group.</summary>
    public class MenuEntry
    {
        public string KeyName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;

        /// <summary>Display name of the group this link currently lives in, or null if it's a direct top-level link.</summary>
        public string? GroupDisplayName { get; set; }

        /// <summary>True for special non-.lnk Send To entries (e.g. "Compressed folder", "Documents") whose
        /// format our simple editor can't safely rewrite — viewable and deletable, but not editable.</summary>
        public bool IsSpecial { get; set; }
    }
}
