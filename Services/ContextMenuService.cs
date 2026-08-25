using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using QuickLaunchMenuWinForms.Models;

namespace QuickLaunchMenuWinForms.Services
{
    /// <summary>
    /// Manages one of the three real Windows right-click menus (desktop background, a folder, or any file).
    /// All three are stored in the registry the same way — this class just points at different roots and,
    /// for Folder/File, auto-passes the clicked item's path to the launched program via %1.
    /// </summary>
    public class ContextMenuService
    {
        private const string DesktopRootSubKey = @"Software\Classes\DesktopBackground\Shell";
        private const string FolderBackgroundRootSubKey = @"Software\Classes\Directory\Background\shell";
        private const string FolderRootSubKey = @"Software\Classes\Directory\shell";
        private const string FileRootSubKey = @"Software\Classes\*\shell";
        private const string LinkPrefix = "QL_";
        private const string GroupPrefix = "QLGRP_";
        private const string QuickAddVerbKeyName = "QuickLaunchAdd";

        /// <summary>
        /// A COM context-menu handler decides its own visible text at runtime (that's the whole point of
        /// the COM contract) — it isn't stored anywhere static, so the DLL's internal component name is all
        /// we could otherwise show. For Microsoft's own well-known, stable system components, map the
        /// handler's registry key name to the label users actually recognize from the real menu.
        /// </summary>
        private static readonly Dictionary<string, string> KnownHandlerNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sharing"] = "🔷 Предоставить доступ (Give access to)",
            ["ModernSharing"] = "🔷 Предоставить доступ (Give access to, новое меню)",
            ["Library Location"] = "🔷 Добавить в библиотеку (Include in library)",
            ["EncryptionMenu"] = "🔷 Шифрование EFS (Encrypt/Decrypt)",
            ["CopyAsPathMenu"] = "🔷 Копировать как путь (Copy as path)",
            ["CopyToFolder"] = "🔷 Копировать в папку (Copy to folder)",
            ["MoveToFolder"] = "🔷 Переместить в папку (Move to folder)",
            ["SendTo"] = "🔷 Построение меню «Отправить» (системное)",
            ["Offline Files"] = "🔷 Всегда доступно офлайн / Синхронизация",
            ["WorkFolders"] = "🔷 Рабочие папки (Work Folders)",
            ["PintoStartScreen"] = "🔷 Закрепить на начальном экране (Pin to Start)",
            ["{a2a9545d-a0c2-42b4-9708-a0b2badd77c8}"] = "🔷 Закрепить на начальном экране (Pin to Start)",
            ["{596ab062-b4d2-4215-9f74-e9109b0a8153}"] = "🔷 Восстановить предыдущие версии (Restore previous versions)",
            ["{450d8fba-ad25-11d0-98a8-0800361b1103}"] = "🔷 Добавить в архив / Свойства архивации",
        };

        private readonly MenuScope _scope;
        private readonly string _writeRootPath;
        private readonly (string Path, string Label)[] _readRoots;
        private readonly (string ClassPath, string Label)[] _shellexClassRoots;

        public ContextMenuService(MenuScope scope)
        {
            _scope = scope;

            switch (scope)
            {
                case MenuScope.Folder:
                    _writeRootPath = FolderRootSubKey;
                    _readRoots = new[] { (FolderRootSubKey, "Папки") };
                    _shellexClassRoots = new[]
                    {
                        (@"Software\Classes\Directory", "Папки"),
                        (@"Software\Classes\Folder", "Папки (тип Folder)"),
                        (@"Software\Classes\AllFilesystemObjects", "Файлы и папки вместе"),
                    };
                    break;
                case MenuScope.File:
                    _writeRootPath = FileRootSubKey;
                    _readRoots = new[] { (FileRootSubKey, "Любые файлы") };
                    _shellexClassRoots = new[]
                    {
                        (@"Software\Classes\*", "Любые файлы"),
                        (@"Software\Classes\AllFilesystemObjects", "Файлы и папки вместе"),
                    };
                    break;
                default:
                    _writeRootPath = DesktopRootSubKey;
                    _readRoots = new[]
                    {
                        (DesktopRootSubKey, "Рабочий стол"),
                        (FolderBackgroundRootSubKey, "Везде — папки и рабочий стол"),
                    };
                    _shellexClassRoots = new[]
                    {
                        (@"Software\Classes\DesktopBackground", "Рабочий стол"),
                        (@"Software\Classes\Directory\Background", "Везде — папки и рабочий стол"),
                    };
                    break;
            }
        }

        /// <summary>The registry path this instance writes new entries to — used by the live-refresh watcher.</summary>
        public string WriteRootPath => _writeRootPath;

        /// <summary>True for Folder/File scopes, where the clicked item's path is auto-passed as %1.</summary>
        public bool UsesClickedItemPlaceholder => _scope != MenuScope.Desktop;

        // -------------------- Reading the full real menu (ours + everyone else's) --------------------

        /// <summary>Every entry that actually shows up in this menu — HKCU (yours + other apps) and HKLM (system-wide, read-only here).</summary>
        public List<MenuNode> GetMenuTree()
        {
            MigrateLegacySubmenu();

            var nodes = new List<MenuNode>();

            foreach (var (rootPath, label) in _readRoots)
            {
                using (var hkcuRoot = Registry.CurrentUser.OpenSubKey(rootPath, writable: false))
                {
                    if (hkcuRoot != null)
                    {
                        foreach (var keyName in hkcuRoot.GetSubKeyNames())
                        {
                            if (keyName == QuickAddVerbKeyName) continue; // our own "Добавить в Быстрый запуск" helper — not a real entry
                            var node = ReadNode(hkcuRoot, keyName, isProtected: false, sourceRootPath: rootPath, sourceLabel: label);
                            if (node != null) nodes.Add(node);
                        }
                    }
                }

                using (var hklmRoot = Registry.LocalMachine.OpenSubKey(rootPath, writable: false))
                {
                    if (hklmRoot != null)
                    {
                        foreach (var keyName in hklmRoot.GetSubKeyNames())
                        {
                            if (keyName == QuickAddVerbKeyName) continue;
                            var node = ReadNode(hklmRoot, keyName, isProtected: true, sourceRootPath: rootPath, sourceLabel: label);
                            if (node != null) nodes.Add(node);
                        }
                    }
                }
            }

            return nodes.OrderBy(n => n.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>
        /// COM-based shell extensions (shellex\ContextMenuHandlers). Their actual menu items can't be listed —
        /// that would require running the extension's own code — but they can be safely toggled on/off using
        /// the same reversible "-CLSID" trick ShellExView and similar tools use.
        /// </summary>
        public List<MenuNode> GetExtensions()
        {
            var nodes = new List<MenuNode>();

            foreach (var (classPath, label) in _shellexClassRoots)
            {
                CollectExtensions(Registry.CurrentUser, classPath, label, isHklm: false, nodes);
                CollectExtensions(Registry.LocalMachine, classPath, label, isHklm: true, nodes);
            }

            return nodes.OrderBy(n => n.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static void CollectExtensions(RegistryKey hive, string classPath, string label, bool isHklm, List<MenuNode> nodes)
        {
            var handlersPath = classPath + @"\shellex\ContextMenuHandlers";
            using (var handlersKey = hive.OpenSubKey(handlersPath, writable: false))
            {
                if (handlersKey == null) return;

                foreach (var name in handlersKey.GetSubKeyNames())
                {
                    using (var handlerKey = handlersKey.OpenSubKey(name))
                    {
                        var rawClsid = handlerKey?.GetValue(null) as string;
                        if (string.IsNullOrWhiteSpace(rawClsid)) continue;

                        var info = new ExtensionInfo
                        {
                            HandlerSubPath = handlersPath.Substring(@"Software\Classes\".Length) + "\\" + name,
                            IsHklm = isHklm,
                            RawClsid = rawClsid!
                        };

                        ResolveClsidInfo(info);

                        string displayName;
                        if (KnownHandlerNames.TryGetValue(name, out var knownName))
                        {
                            // The COM object decides its own menu text at runtime — it's not stored anywhere
                            // static, so for well-known Microsoft components we show the real label users
                            // recognize instead of the DLL's internal component name.
                            displayName = knownName;
                        }
                        else
                        {
                            displayName = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription! : name;
                            if (!string.IsNullOrWhiteSpace(info.Company)) displayName += $"  ({info.Company})";
                        }

                        nodes.Add(new MenuNode
                        {
                            KeyName = name,
                            DisplayName = displayName!,
                            IsGroup = false,
                            IsOwned = false,
                            IsProtected = false,
                            IsExtension = true,
                            SourceRootPath = handlersPath,
                            SourceLabel = label,
                            Extension = info
                        });
                    }
                }
            }
        }

        private static void ResolveClsidInfo(ExtensionInfo info)
        {
            var clsidPath = @"Software\Classes\CLSID\" + info.Clsid;

            string? friendlyName = null;
            string? dllPath = null;

            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using (var clsidKey = hive.OpenSubKey(clsidPath, writable: false))
                {
                    if (clsidKey == null) continue;

                    friendlyName ??= clsidKey.GetValue(null) as string;
                    using (var inproc = clsidKey.OpenSubKey("InprocServer32"))
                    {
                        dllPath ??= inproc?.GetValue(null) as string;
                    }
                    if (friendlyName != null && dllPath != null) break;
                }
            }

            info.DllPath = dllPath;

            if (!string.IsNullOrWhiteSpace(dllPath))
            {
                try
                {
                    var expanded = Environment.ExpandEnvironmentVariables(dllPath!);
                    if (File.Exists(expanded))
                    {
                        var versionInfo = FileVersionInfo.GetVersionInfo(expanded);
                        info.FileDescription = !string.IsNullOrWhiteSpace(versionInfo.FileDescription)
                            ? versionInfo.FileDescription
                            : friendlyName;
                        info.Company = versionInfo.CompanyName;
                        return;
                    }
                }
                catch
                {
                    // Fall through to the registry-only friendly name below.
                }
            }

            info.FileDescription = friendlyName;
        }

        /// <summary>Enables or disables a shell extension using the standard reversible "-CLSID" prefix.
        /// Throws UnauthorizedAccessException if this process lacks write access (HKLM needs elevation).</summary>
        public void SetExtensionEnabled(ExtensionInfo extension, bool enabled)
        {
            var hive = extension.IsHklm ? Registry.LocalMachine : Registry.CurrentUser;
            using (var key = hive.OpenSubKey(@"Software\Classes\" + extension.HandlerSubPath, writable: true))
            {
                if (key == null) throw new InvalidOperationException("Не удалось найти запись расширения в реестре.");
                key.SetValue(null, enabled ? extension.Clsid : "-" + extension.Clsid);
            }
        }

        public List<string> GetOwnedGroupNames()
        {
            using (var root = Registry.CurrentUser.OpenSubKey(_writeRootPath, writable: false))
            {
                if (root == null) return new List<string>();

                return root.GetSubKeyNames()
                    .Where(k => k.StartsWith(GroupPrefix, StringComparison.Ordinal))
                    .Select(k =>
                    {
                        using (var groupKey = root.OpenSubKey(k))
                        {
                            return groupKey?.GetValue("MUIVerb") as string ?? k.Substring(GroupPrefix.Length);
                        }
                    })
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
        }

        public bool KeyNameExists(string keyName, string? groupDisplayName)
        {
            var parentPath = ResolveParentPath(groupDisplayName);
            using (var parent = Registry.CurrentUser.OpenSubKey(parentPath, writable: false))
            using (var entry = parent?.OpenSubKey(keyName))
            {
                return entry != null;
            }
        }

        public string BuildLinkKeyName(string displayName) => LinkPrefix + SanitizeKeyName(displayName);

        // -------------------- Writing --------------------

        public void AddOrUpdateLink(
            string? existingKeyName, string? existingGroupDisplayName,
            string displayName, string targetPath, string arguments, string? iconPath,
            string? newGroupDisplayName)
        {
            if (!string.IsNullOrWhiteSpace(newGroupDisplayName))
            {
                EnsureGroup(newGroupDisplayName!);
            }

            var newParentPath = ResolveParentPath(newGroupDisplayName);
            var newKeyName = BuildLinkKeyName(displayName);

            using (var newParent = Registry.CurrentUser.CreateSubKey(newParentPath, writable: true))
            {
                if (newParent == null) throw new InvalidOperationException("Не удалось открыть раздел реестра HKCU.");

                var oldParentPath = ResolveParentPath(existingGroupDisplayName);
                var isMovingOrRenaming = existingKeyName != null &&
                    (!string.Equals(existingKeyName, newKeyName, StringComparison.Ordinal) || !string.Equals(oldParentPath, newParentPath, StringComparison.OrdinalIgnoreCase));

                if (isMovingOrRenaming)
                {
                    using (var oldParent = Registry.CurrentUser.OpenSubKey(oldParentPath, writable: true))
                    {
                        oldParent?.DeleteSubKeyTree(existingKeyName!, throwOnMissingSubKey: false);
                    }
                }

                using (var entryKey = newParent.CreateSubKey(newKeyName, writable: true))
                {
                    if (entryKey == null) throw new InvalidOperationException("Не удалось создать пункт меню.");

                    entryKey.SetValue("MUIVerb", displayName);

                    var iconResolved = string.IsNullOrWhiteSpace(iconPath) ? $"{targetPath},0" : iconPath;
                    entryKey.SetValue("Icon", iconResolved);

                    using (var cmdKey = entryKey.CreateSubKey("command", writable: true))
                    {
                        if (cmdKey == null) throw new InvalidOperationException("Не удалось задать команду запуска.");
                        cmdKey.SetValue(null, BuildCommandLine(targetPath, arguments));
                    }
                }

                if (isMovingOrRenaming && !string.IsNullOrWhiteSpace(existingGroupDisplayName))
                {
                    CleanupGroupIfEmpty(existingGroupDisplayName!);
                }
            }
        }

        /// <summary>Deletes a node (link or whole group with its children). Only call for non-protected nodes.</summary>
        public void RemoveNode(MenuNode node)
        {
            if (node.IsProtected)
            {
                throw new InvalidOperationException("Пункты из HKEY_LOCAL_MACHINE нельзя удалять из этой программы без прав администратора.");
            }

            using (var root = Registry.CurrentUser.OpenSubKey(node.SourceRootPath, writable: true))
            {
                root?.DeleteSubKeyTree(node.KeyName, throwOnMissingSubKey: false);
            }
        }

        /// <summary>Deletes a "protected" (HKLM) node directly. Only succeeds when this process is already
        /// elevated — otherwise throws UnauthorizedAccessException so the caller can fall back to the
        /// elevated helper (see ElevatedRegistryClient.TryDeleteTree, using SourceRootPath + "\" + KeyName).</summary>
        public void RemoveProtectedNodeDirect(MenuNode node)
        {
            using (var root = Registry.LocalMachine.OpenSubKey(node.SourceRootPath, writable: true))
            {
                if (root == null) throw new UnauthorizedAccessException("Нет доступа на запись в HKEY_LOCAL_MACHINE.");
                root.DeleteSubKeyTree(node.KeyName, throwOnMissingSubKey: false);
            }
        }

        /// <summary>Registry writes needed to update an existing HKLM ("protected") link's target/icon/
        /// display name IN PLACE — same key, no rename or group move (unlike owned links, a foreign
        /// program's key name has nothing to do with its display name, so none of that is needed here).
        /// Caller executes these via ElevatedRegistryClient.TrySetValues (or directly, if already elevated).</summary>
        public List<ElevatedRegistryWrite> BuildProtectedLinkUpdate(MenuNode node, string displayName, string targetPath, string arguments, string? iconPath)
        {
            var entryPath = node.SourceRootPath + "\\" + node.KeyName;
            var iconResolved = string.IsNullOrWhiteSpace(iconPath) ? $"{targetPath},0" : iconPath;

            return new List<ElevatedRegistryWrite>
            {
                new ElevatedRegistryWrite { Path = entryPath, Name = "MUIVerb", Value = displayName },
                new ElevatedRegistryWrite { Path = entryPath, Name = "Icon", Value = iconResolved! },
                new ElevatedRegistryWrite { Path = entryPath + "\\command", Name = null, Value = BuildCommandLine(targetPath, arguments) }
            };
        }

        /// <summary>Registers "Добавить в Быстрый запуск" on the right-click menu of any file and any folder
        /// (feeds the Desktop tab's list — always writes there regardless of which scope instance calls it).</summary>
        public void EnsureFileFolderContextMenu()
        {
            RegisterAddVerb(FileRootSubKey + "\\" + QuickAddVerbKeyName);
            RegisterAddVerb(FolderRootSubKey + "\\" + QuickAddVerbKeyName);
        }

        private static void RegisterAddVerb(string keyPath)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true))
            {
                if (key == null) return;

                var exePath = GetAppExecutablePath();
                key.SetValue("MUIVerb", "Добавить в Быстрый запуск");
                key.SetValue("Icon", $"{exePath},0");

                using (var cmd = key.CreateSubKey("command", writable: true))
                {
                    cmd?.SetValue(null, $"\"{exePath}\" --add \"%1\"");
                }
            }
        }

        // -------------------- Internals --------------------

        /// <summary>One-time cleanup: an earlier version of this tool grouped every desktop entry under one
        /// "QuickLaunchMenu" flyout that didn't expand reliably. Flatten it back to direct top-level links.</summary>
        private void MigrateLegacySubmenu()
        {
            const string legacyGroupKeyName = "QuickLaunchMenu";

            using (var root = Registry.CurrentUser.OpenSubKey(_writeRootPath, writable: true))
            {
                if (root == null) return;

                bool exists;
                using (var legacyGroup = root.OpenSubKey(legacyGroupKeyName)) exists = legacyGroup != null;
                if (!exists) return;

                using (var legacyShell = root.OpenSubKey(legacyGroupKeyName + "\\Shell"))
                {
                    if (legacyShell != null)
                    {
                        foreach (var childName in legacyShell.GetSubKeyNames())
                        {
                            CopyKeyRecursive(legacyShell, childName, root, childName);
                        }
                    }
                }

                root.DeleteSubKeyTree(legacyGroupKeyName, throwOnMissingSubKey: false);
            }
        }

        private static void CopyKeyRecursive(RegistryKey sourceParent, string sourceKeyName, RegistryKey destParent, string destKeyName)
        {
            using (var source = sourceParent.OpenSubKey(sourceKeyName))
            {
                if (source == null) return;

                using (var dest = destParent.CreateSubKey(destKeyName, writable: true))
                {
                    if (dest == null) return;

                    foreach (var valueName in source.GetValueNames())
                    {
                        dest.SetValue(valueName, source.GetValue(valueName));
                    }
                    foreach (var subKeyName in source.GetSubKeyNames())
                    {
                        CopyKeyRecursive(source, subKeyName, dest, subKeyName);
                    }
                }
            }
        }

        private void EnsureGroup(string groupDisplayName)
        {
            var groupKeyName = BuildGroupKeyName(groupDisplayName);
            using (var groupKey = Registry.CurrentUser.CreateSubKey(_writeRootPath + "\\" + groupKeyName, writable: true))
            {
                if (groupKey == null) throw new InvalidOperationException("Не удалось создать группу меню.");

                groupKey.SetValue("MUIVerb", groupDisplayName);
                groupKey.SetValue("Icon", $"{GetAppExecutablePath()},0");
                groupKey.SetValue("SubCommands", string.Empty);
                using (groupKey.CreateSubKey("Shell", writable: true)) { }
            }
        }

        private void CleanupGroupIfEmpty(string groupDisplayName)
        {
            var groupKeyName = BuildGroupKeyName(groupDisplayName);
            using (var shellKey = Registry.CurrentUser.OpenSubKey(_writeRootPath + "\\" + groupKeyName + "\\Shell", writable: false))
            {
                if (shellKey != null && shellKey.GetSubKeyNames().Length > 0) return;
            }

            using (var root = Registry.CurrentUser.OpenSubKey(_writeRootPath, writable: true))
            {
                root?.DeleteSubKeyTree(groupKeyName, throwOnMissingSubKey: false);
            }
        }

        private string ResolveParentPath(string? groupDisplayName)
        {
            if (string.IsNullOrWhiteSpace(groupDisplayName)) return _writeRootPath;
            return _writeRootPath + "\\" + BuildGroupKeyName(groupDisplayName!) + "\\Shell";
        }

        private static string BuildGroupKeyName(string groupDisplayName) => GroupPrefix + SanitizeKeyName(groupDisplayName);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

        /// <summary>Windows often stores menu text as an "indirect string" pointing at a resource inside a DLL,
        /// e.g. "@shell32.dll,-8506" — Explorer resolves this to the real localized text at render time instead
        /// of showing it as-is. We do the same, so the preview shows "Открыть в Терминале" instead of the raw
        /// resource reference.</summary>
        private static string? ResolveIndirectString(string source)
        {
            try
            {
                var buffer = new StringBuilder(1024);
                var expanded = Environment.ExpandEnvironmentVariables(source);
                var result = SHLoadIndirectString(expanded, buffer, buffer.Capacity, IntPtr.Zero);
                return result == 0 && buffer.Length > 0 ? buffer.ToString() : null;
            }
            catch
            {
                return null;
            }
        }

        private static string GetDisplayName(RegistryKey key, string keyName)
        {
            var raw = key.GetValue("MUIVerb") as string
                ?? key.GetValue(null) as string   // some tools (e.g. Everything, Tabby) only set the (Default) value
                ?? keyName;

            if (!string.IsNullOrEmpty(raw) && raw[0] == '@')
            {
                var resolved = ResolveIndirectString(raw);
                return string.IsNullOrEmpty(resolved) ? keyName : resolved!;
            }

            return raw;
        }

        private MenuNode? ReadNode(RegistryKey parent, string keyName, bool isProtected, string sourceRootPath, string sourceLabel)
        {
            using (var key = parent.OpenSubKey(keyName))
            {
                if (key == null) return null;

                var displayName = GetDisplayName(key, keyName);
                var isOwnedGroup = keyName.StartsWith(GroupPrefix, StringComparison.Ordinal);

                var subKeyNames = key.GetSubKeyNames();
                var hasCommand = subKeyNames.Contains("command");
                var hasShell = subKeyNames.Contains("Shell");

                if (hasShell)
                {
                    var node = new MenuNode
                    {
                        KeyName = keyName,
                        DisplayName = displayName,
                        IsGroup = true,
                        IsOwned = isOwnedGroup,
                        IsProtected = isProtected,
                        SourceRootPath = sourceRootPath,
                        SourceLabel = sourceLabel
                    };

                    var childSourceRootPath = sourceRootPath + "\\" + keyName + "\\Shell";
                    using (var shellKey = key.OpenSubKey("Shell"))
                    {
                        if (shellKey != null)
                        {
                            foreach (var childName in shellKey.GetSubKeyNames())
                            {
                                var child = ReadLinkNode(shellKey, childName, displayName, isProtected, childSourceRootPath, sourceLabel);
                                if (child != null) node.Children.Add(child);
                            }
                        }
                    }

                    node.Children = node.Children.OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
                    return node;
                }

                if (hasCommand)
                {
                    return ReadLinkNode(parent, keyName, null, isProtected, sourceRootPath, sourceLabel);
                }

                // Verb with neither Shell nor command (COM/dynamic handler, e.g. a shell extension DLL) —
                // show it so it's not a mystery, but there's nothing we can safely edit or expand.
                return new MenuNode
                {
                    KeyName = keyName,
                    DisplayName = displayName,
                    IsGroup = false,
                    IsOwned = false,
                    IsProtected = isProtected,
                    SourceRootPath = sourceRootPath,
                    SourceLabel = sourceLabel,
                    Link = null
                };
            }
        }

        private MenuNode? ReadLinkNode(RegistryKey parentOfLink, string keyName, string? groupDisplayName, bool isProtected, string sourceRootPath, string sourceLabel)
        {
            using (var entryKey = parentOfLink.OpenSubKey(keyName))
            {
                if (entryKey == null) return null;

                var displayName = GetDisplayName(entryKey, keyName);
                var iconValue = entryKey.GetValue("Icon") as string ?? string.Empty;

                string commandLine;
                using (var cmdKey = entryKey.OpenSubKey("command"))
                {
                    commandLine = cmdKey?.GetValue(null) as string ?? string.Empty;
                }

                var split = SplitCommandLine(commandLine);
                var isOwned = keyName.StartsWith(LinkPrefix, StringComparison.Ordinal);

                return new MenuNode
                {
                    KeyName = keyName,
                    DisplayName = displayName,
                    IsGroup = false,
                    IsOwned = isOwned,
                    IsProtected = isProtected,
                    SourceRootPath = sourceRootPath,
                    SourceLabel = sourceLabel,
                    Link = new MenuEntry
                    {
                        KeyName = keyName,
                        DisplayName = displayName,
                        TargetPath = split.Item1,
                        Arguments = split.Item2,
                        IconPath = StripIconIndex(iconValue),
                        GroupDisplayName = groupDisplayName
                    }
                };
            }
        }

        /// <summary>Builds the registry command string for a new/edited link, in the style this instance's scope needs.</summary>
        private string BuildCommandLine(string targetPath, string arguments)
        {
            if (_scope == MenuScope.Desktop)
            {
                var isDirectlyRunnable = false;
                var ext = Path.GetExtension(targetPath);
                if (!string.IsNullOrEmpty(ext))
                {
                    ext = ext.ToLowerInvariant();
                    isDirectlyRunnable = ext == ".exe" || ext == ".com" || ext == ".bat" || ext == ".cmd";
                }

                if (!isDirectlyRunnable)
                {
                    // Folders and documents can't be launched via CreateProcess directly — hand them to Explorer,
                    // which opens folders and resolves default file/shortcut associations correctly.
                    return $"explorer.exe \"{targetPath}\"";
                }

                var desktopCommandLine = $"\"{targetPath}\"";
                if (!string.IsNullOrWhiteSpace(arguments))
                {
                    desktopCommandLine += " " + arguments;
                }
                return desktopCommandLine;
            }

            // Folder/File scope: the target is always the launcher program, and the clicked item's path is
            // handed to it via %1 (Explorer fills this in). If the user already typed %1 somewhere in their
            // own arguments, trust their placement instead of appending a second one.
            var commandLine = $"\"{targetPath}\"";
            if (!string.IsNullOrWhiteSpace(arguments) && arguments.IndexOf("%1", StringComparison.Ordinal) >= 0)
            {
                commandLine += " " + arguments;
            }
            else
            {
                commandLine += " \"%1\"";
                if (!string.IsNullOrWhiteSpace(arguments)) commandLine += " " + arguments;
            }
            return commandLine;
        }

        private static string GetAppExecutablePath()
        {
            return Assembly.GetEntryAssembly()?.Location ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        }

        private static string SanitizeKeyName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var c in name.Trim())
            {
                sb.Append(invalid.Contains(c) || c == '\\' || c == '/' ? '_' : c);
            }
            var clean = sb.ToString().Replace(' ', '_');
            return string.IsNullOrWhiteSpace(clean) ? "Entry" : clean;
        }

        private static string StripIconIndex(string iconValue)
        {
            if (string.IsNullOrWhiteSpace(iconValue)) return string.Empty;
            var lastComma = iconValue.LastIndexOf(',');
            return lastComma > 0 ? iconValue.Substring(0, lastComma) : iconValue;
        }

        /// <summary>Splits a registry command string back into (target, arguments) for display/editing.
        /// For Folder/File scope, a bare trailing "%1" that we auto-appended is hidden from Arguments.</summary>
        private Tuple<string, string> SplitCommandLine(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return Tuple.Create(string.Empty, string.Empty);

            commandLine = commandLine.Trim();

            const string explorerPrefix = "explorer.exe \"";
            if (_scope == MenuScope.Desktop && commandLine.StartsWith(explorerPrefix, StringComparison.OrdinalIgnoreCase) && commandLine.EndsWith("\""))
            {
                return Tuple.Create(commandLine.Substring(explorerPrefix.Length, commandLine.Length - explorerPrefix.Length - 1), string.Empty);
            }

            string target;
            string rest;

            if (commandLine.StartsWith("\""))
            {
                var closingQuote = commandLine.IndexOf('"', 1);
                if (closingQuote > 0)
                {
                    target = commandLine.Substring(1, closingQuote - 1);
                    rest = commandLine.Substring(closingQuote + 1).Trim();
                }
                else
                {
                    target = commandLine;
                    rest = string.Empty;
                }
            }
            else
            {
                var firstSpace = commandLine.IndexOf(' ');
                target = firstSpace < 0 ? commandLine : commandLine.Substring(0, firstSpace);
                rest = firstSpace < 0 ? string.Empty : commandLine.Substring(firstSpace + 1).Trim();
            }

            if (_scope != MenuScope.Desktop)
            {
                if (string.Equals(rest, "\"%1\"", StringComparison.OrdinalIgnoreCase) || string.Equals(rest, "%1", StringComparison.OrdinalIgnoreCase))
                {
                    rest = string.Empty;
                }
                else if (rest.StartsWith("\"%1\" ", StringComparison.OrdinalIgnoreCase))
                {
                    rest = rest.Substring(5).Trim();
                }
                else if (rest.StartsWith("%1 ", StringComparison.OrdinalIgnoreCase))
                {
                    rest = rest.Substring(3).Trim();
                }
            }

            return Tuple.Create(target, rest);
        }
    }
}
