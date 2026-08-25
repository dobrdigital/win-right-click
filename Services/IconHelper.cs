using System;
using System.Drawing;
using System.IO;

namespace QuickLaunchMenuWinForms.Services
{
    public static class IconHelper
    {
        /// <summary>Best-effort icon lookup for a program/shortcut/.ico path. Returns null if it can't be read.</summary>
        public static Icon? GetIconFor(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            try
            {
                var ext = Path.GetExtension(path);
                if (string.Equals(ext, ".ico", StringComparison.OrdinalIgnoreCase))
                {
                    return new Icon(path);
                }
                return Icon.ExtractAssociatedIcon(path);
            }
            catch
            {
                return null;
            }
        }
    }
}
