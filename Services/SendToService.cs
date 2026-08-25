using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using QuickLaunchMenuWinForms.Models;

namespace QuickLaunchMenuWinForms.Services
{
    /// <summary>
    /// Manages the "Send To" submenu — unlike every other menu in this app, it isn't registry-based at all:
    /// every .lnk shortcut placed in %APPDATA%\Microsoft\Windows\SendTo becomes an entry, with Windows
    /// automatically appending the clicked file's path as an argument when it's invoked. Per-user, no
    /// admin rights ever needed.
    /// </summary>
    public class SendToService
    {
        public string FolderPath { get; } = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);

        public List<MenuEntry> GetEntries()
        {
            var result = new List<MenuEntry>();
            if (!Directory.Exists(FolderPath)) return result;

            foreach (var file in Directory.GetFiles(FolderPath))
            {
                var fileName = Path.GetFileName(file);
                if (string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                var name = Path.GetFileNameWithoutExtension(file);
                var ext = Path.GetExtension(file);
                var isLnk = string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase);

                if (isLnk)
                {
                    var (target, args, icon) = ReadShortcut(file);
                    result.Add(new MenuEntry
                    {
                        KeyName = name,
                        DisplayName = name,
                        TargetPath = target,
                        Arguments = args,
                        IconPath = icon
                    });
                }
                else
                {
                    // Windows built-ins like "Compressed (zipped) Folder.ZFSendToTarget",
                    // "Desktop (create shortcut).DeskLink", "Documents.mydocs", "Mail Recipient.MAPIMail" —
                    // not plain shortcuts, so we can't safely read/rewrite their target. Still show + allow delete.
                    result.Add(new MenuEntry
                    {
                        KeyName = fileName,
                        DisplayName = name,
                        TargetPath = $"(системный пункт Windows — файл {ext})",
                        Arguments = string.Empty,
                        IconPath = string.Empty,
                        IsSpecial = true
                    });
                }
            }

            return result.OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public bool NameExists(string displayName) =>
            File.Exists(Path.Combine(FolderPath, SanitizeFileName(displayName) + ".lnk"));

        public void AddOrUpdate(string? existingName, string displayName, string targetPath, string arguments, string? iconPath)
        {
            Directory.CreateDirectory(FolderPath);

            var newPath = Path.Combine(FolderPath, SanitizeFileName(displayName) + ".lnk");

            if (existingName != null)
            {
                var oldPath = Path.Combine(FolderPath, existingName + ".lnk");
                if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }
            }

            WriteShortcut(newPath, targetPath, arguments, iconPath);
        }

        public void Remove(string keyName)
        {
            // Regular entries store the name without extension (append .lnk); special entries store
            // their real full file name (already has an extension) — see GetEntries().
            var path = Path.HasExtension(keyName)
                ? Path.Combine(FolderPath, keyName)
                : Path.Combine(FolderPath, keyName + ".lnk");
            if (File.Exists(path)) File.Delete(path);
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var c in name.Trim())
            {
                sb.Append(invalid.Contains(c) ? '_' : c);
            }
            var clean = sb.ToString();
            return string.IsNullOrWhiteSpace(clean) ? "Entry" : clean;
        }

        private static (string target, string args, string icon) ReadShortcut(string path)
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return (string.Empty, string.Empty, string.Empty);

                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic sc = shell.CreateShortcut(path);

                string target = sc.TargetPath ?? string.Empty;
                string args = sc.Arguments ?? string.Empty;
                string iconLocation = sc.IconLocation ?? string.Empty;

                var icon = string.Empty;
                if (!string.IsNullOrWhiteSpace(iconLocation))
                {
                    var comma = iconLocation.LastIndexOf(',');
                    icon = comma > 0 ? iconLocation.Substring(0, comma) : iconLocation;
                }

                return (target, args, icon);
            }
            catch
            {
                return (string.Empty, string.Empty, string.Empty);
            }
        }

        private static void WriteShortcut(string path, string targetPath, string arguments, string? iconPath)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("WScript.Shell недоступен в этой системе.");

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic sc = shell.CreateShortcut(path);

            sc.TargetPath = targetPath;
            sc.Arguments = arguments ?? string.Empty;

            var iconResolved = string.IsNullOrWhiteSpace(iconPath) ? targetPath : iconPath;
            sc.IconLocation = iconResolved + ",0";

            sc.Save();
        }
    }
}
