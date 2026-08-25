using System.Collections.Generic;

namespace QuickLaunchMenuWinForms.Models
{
    /// <summary>One row of the real desktop context menu — either a direct link or a group (flyout) of links.</summary>
    public class MenuNode
    {
        public string KeyName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsGroup { get; set; }

        /// <summary>Created and managed by this tool (safe to edit).</summary>
        public bool IsOwned { get; set; }

        /// <summary>Registered machine-wide (HKLM) — never editable/deletable from here.</summary>
        public bool IsProtected { get; set; }

        /// <summary>Registry path this node's key directly lives under (needed to delete/re-open it correctly).</summary>
        public string SourceRootPath { get; set; } = string.Empty;

        /// <summary>Human label for where this entry shows up (e.g. "Рабочий стол" vs "Везде — папки и рабочий стол").</summary>
        public string SourceLabel { get; set; } = string.Empty;

        /// <summary>Populated when IsGroup is false and the entry is a plain launchable link.</summary>
        public MenuEntry? Link { get; set; }

        /// <summary>True when this row represents a COM shell extension rather than a classic verb.</summary>
        public bool IsExtension { get; set; }

        /// <summary>Populated when IsExtension is true.</summary>
        public ExtensionInfo? Extension { get; set; }

        public List<MenuNode> Children { get; set; } = new List<MenuNode>();
    }
}
