using System;
using System.Collections.Generic;
using System.IO;

namespace QuickLaunchMenuWinForms.Services
{
    /// <summary>Resolves a menu entry's target the way Windows itself does when launching it. A full path
    /// is used as-is, but a bare executable name with no directory (e.g. "cmd.exe", "wsl.exe" — exactly what
    /// several built-in "Open X here" verbs store, relying on the shell to find it) has to be searched for
    /// via %SystemRoot%\System32 and PATH instead of treated as a literal relative path.</summary>
    public static class PathResolver
    {
        public static string? ResolveExisting(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            var expanded = Environment.ExpandEnvironmentVariables(path);
            if (Directory.Exists(expanded) || File.Exists(expanded)) return expanded;

            // Already has a directory component and still wasn't found above — no PATH search applies.
            if (!string.IsNullOrEmpty(Path.GetDirectoryName(expanded))) return null;

            var candidateDirs = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.SystemX86)
            };
            candidateDirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator));

            foreach (var dir in candidateDirs)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var candidate = Path.Combine(dir, expanded);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                    // Malformed PATH entry — ignore and keep looking.
                }
            }

            return null;
        }
    }
}
