using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using QuickLaunchMenuWinForms.Models;

namespace QuickLaunchMenuWinForms.Services
{
    /// <summary>Plain-JSON backup/restore for entries created by this tool, using the JSON serializer built into .NET Framework.</summary>
    public static class MenuExportService
    {
        private class ExportedEntry
        {
            public string Name { get; set; } = string.Empty;
            public string Target { get; set; } = string.Empty;
            public string Arguments { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public string Group { get; set; } = string.Empty;
        }

        /// <summary>Pulls only the links this tool owns (top-level and inside owned groups) out of the full menu tree.</summary>
        public static List<MenuEntry> FlattenOwnedLinks(IEnumerable<MenuNode> tree)
        {
            var result = new List<MenuEntry>();
            foreach (var node in tree)
            {
                if (node.IsGroup)
                {
                    if (!node.IsOwned) continue;
                    foreach (var child in node.Children)
                    {
                        if (child.IsOwned && child.Link != null) result.Add(child.Link);
                    }
                }
                else if (node.IsOwned && node.Link != null)
                {
                    result.Add(node.Link);
                }
            }
            return result;
        }

        public static void Export(string filePath, IEnumerable<MenuEntry> entries)
        {
            var exported = entries.Select(e => new ExportedEntry
            {
                Name = e.DisplayName,
                Target = e.TargetPath,
                Arguments = e.Arguments,
                Icon = e.IconPath,
                Group = e.GroupDisplayName ?? string.Empty
            }).ToList();

            var serializer = new JavaScriptSerializer();
            File.WriteAllText(filePath, serializer.Serialize(exported));
        }

        public static List<MenuEntry> Import(string filePath)
        {
            var json = File.ReadAllText(filePath);
            var serializer = new JavaScriptSerializer();
            var imported = serializer.Deserialize<List<ExportedEntry>>(json) ?? new List<ExportedEntry>();

            return imported.Select(e => new MenuEntry
            {
                DisplayName = e.Name ?? string.Empty,
                TargetPath = e.Target ?? string.Empty,
                Arguments = e.Arguments ?? string.Empty,
                IconPath = e.Icon ?? string.Empty,
                GroupDisplayName = string.IsNullOrWhiteSpace(e.Group) ? null : e.Group
            }).ToList();
        }
    }
}
