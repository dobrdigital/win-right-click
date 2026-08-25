using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using QuickLaunchMenuWinForms.Models;
using QuickLaunchMenuWinForms.Services;

namespace QuickLaunchMenuWinForms
{
    /// <summary>One full menu-management view (list + preview + buttons) for a single menu scope
    /// (desktop background, folder, or file). Watches the registry so it live-refreshes on any change —
    /// including entries added from outside via the file/folder "Add to Quick Launch" verb.
    /// COM shell extensions (7-Zip, "give access to", etc.) live in the separate "Extensions" tab —
    /// see ExtensionsPanel — since their menu text can't be resolved from the registry, they'd otherwise
    /// show up under confusing internal component names buried among everything else here.</summary>
    public class MenuManagerPanel : UserControl
    {
        private readonly MenuScope _scope;
        private readonly ContextMenuService _service;
        private readonly ImageList _icons = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };

        private ListView _listView = null!;
        private Label _emptyLabel = null!;
        private MenuPreviewControl _preview = null!;

        private Thread? _watcherThread;
        private volatile bool _stopWatching;

        public MenuManagerPanel(MenuScope scope, string title, string subtitle)
        {
            _scope = scope;
            _service = new ContextMenuService(scope);

            BuildUi(title, subtitle);

            if (_scope == MenuScope.Desktop)
            {
                try { _service.EnsureFileFolderContextMenu(); }
                catch { /* non-fatal — the list still works without the file/folder "add" shortcut */ }
            }

            LoadEntries();
            StartRegistryWatcher();

            Disposed += (s, e) => StopRegistryWatcher();
        }

        private void BuildUi(string title, string subtitle)
        {
            Dock = DockStyle.Fill;
            Font = new Font("Segoe UI", 9F);

            var headerPanel = new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(16, 10, 16, 4) };
            var titleLabel = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 22
            };
            var subtitleLabel = new Label
            {
                Text = subtitle,
                ForeColor = SystemColors.GrayText,
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 42
            };
            headerPanel.Controls.Add(subtitleLabel);
            headerPanel.Controls.Add(titleLabel);

            _listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                MultiSelect = false,
                HideSelection = false,
                SmallImageList = _icons,
                ShowGroups = true
            };
            _listView.Columns.Add(Localization.T("Название", "Name"), 220);
            _listView.Columns.Add(_scope == MenuScope.Desktop
                ? Localization.T("Программа / путь", "Program / path")
                : Localization.T("Программа", "Program"), 300);
            _listView.Columns.Add(Localization.T("Тип", "Type"), 90);
            _listView.Columns.Add("", 34).Tag = "fixed"; // "⋯" — open the containing folder
            _listView.Columns.Add("", 34).Tag = "fixed"; // "?" — search this entry online
            _listView.DoubleClick += (s, e) => EditSelected();
            _listView.MouseUp += (s, e) =>
            {
                OpenContainingFolderIfActionButtonClicked(e);
                SearchOnlineIfActionButtonClicked(e);
            };

            _emptyLabel = new Label
            {
                Text = Localization.T("В меню пока пусто.", "The menu is empty."),
                ForeColor = SystemColors.GrayText,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Visible = false
            };

            var listContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 8, 4) };
            listContainer.Controls.Add(_listView);
            listContainer.Controls.Add(_emptyLabel);

            _preview = new MenuPreviewControl { Dock = DockStyle.Right, Width = 260 };

            // Cross-highlight between the table and the preview mock-up, by MenuNode reference.
            _listView.SelectedIndexChanged += (s, e) => _preview.Highlight(SelectedNode);
            _preview.NodeClicked += node => SelectNodeInList(node);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(16, 8, 16, 8)
            };

            var addButton = new Button { Text = Localization.T("Добавить...", "Add..."), AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            var editButton = new Button { Text = Localization.T("Изменить", "Edit"), AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            var removeButton = new Button { Text = Localization.T("Удалить", "Remove"), AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            var refreshButton = new Button { Text = Localization.T("Обновить", "Refresh"), AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            var exportButton = new Button { Text = Localization.T("Экспорт...", "Export..."), AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(24, 3, 3, 3) };
            var importButton = new Button { Text = Localization.T("Импорт...", "Import..."), AutoSize = true, Padding = new Padding(8, 4, 8, 4) };

            addButton.Click += (s, e) => AddNew();
            editButton.Click += (s, e) => EditSelected();
            removeButton.Click += async (s, e) => await RemoveSelectedAsync();
            refreshButton.Click += (s, e) => LoadEntries();
            exportButton.Click += (s, e) => ExportEntries();
            importButton.Click += (s, e) => ImportEntries();

            buttonPanel.Controls.Add(addButton);
            buttonPanel.Controls.Add(editButton);
            buttonPanel.Controls.Add(removeButton);
            buttonPanel.Controls.Add(refreshButton);
            buttonPanel.Controls.Add(exportButton);
            buttonPanel.Controls.Add(importButton);

            // Dock order: Fill first, then Right/Bottom/Top so the list gets whatever space remains.
            Controls.Add(listContainer);
            Controls.Add(_preview);
            Controls.Add(buttonPanel);
            Controls.Add(headerPanel);
        }

        // -------------------- Live refresh --------------------

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegNotifyChangeKeyValue(
            IntPtr hKey, bool bWatchSubtree, int dwNotifyFilter, IntPtr hEvent, bool fAsynchronous);

        private const int REG_NOTIFY_CHANGE_NAME = 0x1;
        private const int REG_NOTIFY_CHANGE_LAST_SET = 0x4;

        private void StartRegistryWatcher()
        {
            _watcherThread = new Thread(WatchRegistryLoop) { IsBackground = true };
            _watcherThread.Start();
        }

        private void StopRegistryWatcher()
        {
            _stopWatching = true;
            // The thread is parked inside a blocking native call; it's a background thread so it
            // won't keep the process alive — it just exits whenever it next wakes up (or on process exit).
        }

        private void WatchRegistryLoop()
        {
            while (!_stopWatching)
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(_service.WriteRootPath, writable: true))
                    {
                        if (key == null) return;

                        var result = RegNotifyChangeKeyValue(
                            key.Handle.DangerousGetHandle(), true,
                            REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET, IntPtr.Zero, false);

                        if (result != 0 || _stopWatching) return;
                    }

                    if (IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke(new Action(LoadEntries));
                    }
                }
                catch
                {
                    return;
                }
            }
        }

        // -------------------- Data --------------------

        private void LoadEntries()
        {
            if (IsDisposed) return;

            _listView.Items.Clear();
            _listView.Groups.Clear();
            _icons.Images.Clear();

            var tree = RunSafely(() => _service.GetMenuTree(), Localization.T("Не удалось прочитать меню.", "Couldn't read the menu."));
            if (tree == null) return;

            var directGroups = new System.Collections.Generic.Dictionary<string, ListViewGroup>();
            ListViewGroup GetDirectGroup(string sourceLabel)
            {
                if (directGroups.TryGetValue(sourceLabel, out var existing)) return existing;
                var created = new ListViewGroup(Localization.T($"Прямые ссылки — {sourceLabel}", $"Direct links — {sourceLabel}"));
                _listView.Groups.Add(created);
                directGroups[sourceLabel] = created;
                return created;
            }

            foreach (var node in tree)
            {
                if (node.IsGroup)
                {
                    var header = node.DisplayName +
                        (node.IsProtected ? "  🔒" : node.IsOwned ? "" : Localization.T("  (другая программа)", "  (another program)"))
                        + $"  [{node.SourceLabel}]";
                    var group = new ListViewGroup(header);
                    _listView.Groups.Add(group);

                    if (node.Children.Count == 0)
                    {
                        var placeholder = new ListViewItem(Localization.T("(пусто или недоступно для просмотра)", "(empty or unavailable to view)")) { Group = group };
                        placeholder.SubItems.Add(string.Empty);
                        placeholder.SubItems.Add(string.Empty);
                        placeholder.SubItems.Add(string.Empty);
                        placeholder.SubItems.Add(string.Empty);
                        _listView.Items.Add(placeholder);
                    }
                    else
                    {
                        foreach (var child in node.Children)
                        {
                            AddRow(child, group);
                        }
                    }
                }
                else
                {
                    AddRow(node, GetDirectGroup(node.SourceLabel));
                }
            }

            var isEmpty = _listView.Items.Count == 0;
            _emptyLabel.Visible = isEmpty;
            _listView.Visible = !isEmpty;
            Theme.RefreshColumnLayout(_listView);

            _preview.SetEntries(tree);
        }

        private void AddRow(MenuNode node, ListViewGroup group)
        {
            // "Protected" only means this entry lives under HKEY_LOCAL_MACHINE (registered for every user
            // of the machine) — that's just where an installer chose to write it, often because it ran
            // elevated or "for all users" was checked. Plenty of ordinary third-party apps (Notepad++,
            // MPC-HC, FastStone, etc.) end up there — it does NOT mean it's a genuine Windows component.
            var typeLabel = node.IsProtected
                ? Localization.T("🔒 Общий (HKLM)", "🔒 Common (HKLM)")
                : node.IsOwned ? Localization.T("Ваш", "Yours") : Localization.T("Другая программа", "Another program");
            var item = new ListViewItem(node.DisplayName) { Tag = node, Group = group };
            item.SubItems.Add(node.Link?.TargetPath ?? string.Empty);
            item.SubItems.Add(typeLabel);
            item.SubItems.Add(node.Link != null ? "⋯" : string.Empty);
            item.SubItems.Add("?");

            if (!node.IsOwned) item.ForeColor = SystemColors.GrayText;

            var iconSourcePath = node.Link != null
                ? (string.IsNullOrWhiteSpace(node.Link.IconPath) ? node.Link.TargetPath : node.Link.IconPath)
                : null;

            if (!string.IsNullOrWhiteSpace(iconSourcePath))
            {
                if (!_icons.Images.ContainsKey(iconSourcePath))
                {
                    var icon = IconHelper.GetIconFor(iconSourcePath!);
                    if (icon != null) _icons.Images.Add(iconSourcePath, icon);
                }
                if (_icons.Images.ContainsKey(iconSourcePath))
                {
                    item.ImageKey = iconSourcePath;
                }
            }

            _listView.Items.Add(item);
        }

        private MenuNode? SelectedNode =>
            _listView.SelectedItems.Count > 0 ? _listView.SelectedItems[0].Tag as MenuNode : null;

        /// <summary>Selects (and scrolls to) the row whose tag is this exact MenuNode instance —
        /// used when a preview row is clicked, to point back at its row in the table.</summary>
        private void SelectNodeInList(MenuNode node)
        {
            foreach (ListViewItem item in _listView.Items)
            {
                if (ReferenceEquals(item.Tag, node))
                {
                    item.Selected = true;
                    item.EnsureVisible();
                    _listView.Focus();
                    return;
                }
            }
        }

        /// <summary>The "⋯" cell (second-to-last column, added in BuildUi) opens the folder containing the
        /// entry's target and highlights the file itself — SubItems[i].Bounds is reliable for i >= 1 in
        /// Details view even with OwnerDraw, which is what makes hit-testing a specific column this way possible.</summary>
        private void OpenContainingFolderIfActionButtonClicked(MouseEventArgs e)
        {
            var hit = _listView.HitTest(e.Location);
            if (hit.Item == null) return;

            var actionColumnIndex = _listView.Columns.Count - 2;
            if (actionColumnIndex < 1 || actionColumnIndex >= hit.Item.SubItems.Count) return;
            if (!hit.Item.SubItems[actionColumnIndex].Bounds.Contains(e.Location)) return;

            var node = hit.Item.Tag as MenuNode;
            var path = node?.Link?.TargetPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                Info(Localization.T("У этого пункта нет пути к файлу.", "This entry has no file path."));
                return;
            }

            var resolved = PathResolver.ResolveExisting(path!);
            if (resolved == null)
            {
                Info(Localization.T("Файл или папка не найдены — ни по указанному пути, ни в PATH.", "File or folder not found — neither at the given path nor in PATH."));
                return;
            }

            try
            {
                if (Directory.Exists(resolved)) Process.Start("explorer.exe", $"\"{resolved}\"");
                else Process.Start("explorer.exe", $"/select,\"{resolved}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), Localization.T($"Не удалось открыть папку.\n\n{ex.Message}", $"Couldn't open the folder.\n\n{ex.Message}"),
                    Localization.T("Ошибка", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>The "?" cell (last column, added in BuildUi) looks up this entry online — handy for
        /// unfamiliar system-registered verbs where the name alone doesn't say what actually runs.</summary>
        private void SearchOnlineIfActionButtonClicked(MouseEventArgs e)
        {
            var hit = _listView.HitTest(e.Location);
            if (hit.Item == null) return;

            var actionColumnIndex = _listView.Columns.Count - 1;
            if (actionColumnIndex < 1 || actionColumnIndex >= hit.Item.SubItems.Count) return;
            if (!hit.Item.SubItems[actionColumnIndex].Bounds.Contains(e.Location)) return;

            var node = hit.Item.Tag as MenuNode;
            if (node == null) return;

            SearchOnline(node.DisplayName, node.Link?.TargetPath);
        }

        internal static void SearchOnline(string displayName, string? targetPath)
        {
            var queryParts = new System.Collections.Generic.List<string> { displayName };
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                try { queryParts.Add(Path.GetFileName(targetPath)); }
                catch { /* malformed path — skip the filename, the display name alone is still useful */ }
            }
            queryParts.Add(Localization.T("что это за программа и пункт меню", "what is this program and menu entry"));

            try
            {
                Process.Start("https://duckduckgo.com/?q=" + Uri.EscapeDataString(string.Join(" ", queryParts)));
            }
            catch (Exception ex)
            {
                MessageBox.Show(Localization.T($"Не удалось открыть браузер.\n\n{ex.Message}", $"Couldn't open the browser.\n\n{ex.Message}"),
                    Localization.T("Ошибка", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // -------------------- Actions --------------------

        private void AddNew()
        {
            using (var dialog = new AddEditForm(_service, _scope, existingEntry: null))
            {
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    LoadEntries();
                }
            }
        }

        private void EditSelected()
        {
            var node = SelectedNode;
            if (node == null)
            {
                Info(Localization.T("Сначала выбери пункт в списке.", "Select an entry in the list first."));
                return;
            }

            if (node.IsGroup || node.Link == null)
            {
                Info(Localization.T("Группы и системные пункты редактировать нельзя — можно только удалить целиком.",
                    "Groups and non-link entries can't be edited — only removed entirely."));
                return;
            }

            if (!node.IsOwned && !node.IsProtected)
            {
                Info(Localization.T("Этот пункт создан другой программой — его нельзя изменить отсюда, только удалить.",
                    "This entry was created by another program — it can't be edited from here, only removed."));
                return;
            }

            using (var dialog = new AddEditForm(_service, _scope, node.Link, existingNode: node))
            {
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    LoadEntries();
                }
            }
        }

        private async System.Threading.Tasks.Task RemoveSelectedAsync()
        {
            var node = SelectedNode;
            if (node == null)
            {
                Info(Localization.T("Сначала выбери пункт в списке.", "Select an entry in the list first."));
                return;
            }

            if (node.IsProtected)
            {
                await RemoveProtectedNodeAsync(node);
                return;
            }

            string question;
            if (!node.IsOwned)
            {
                question = node.IsGroup
                    ? Localization.T(
                        $"«{node.DisplayName}» — это меню от ДРУГОЙ программы, со всеми пунктами внутри. Удаление отключит её интеграцию с правым кликом. Точно удалить?",
                        $"\"{node.DisplayName}\" is a menu from ANOTHER program, with everything inside it. Removing it will disable that program's right-click integration. Remove it anyway?")
                    : Localization.T(
                        $"«{node.DisplayName}» создан ДРУГОЙ программой, не этим инструментом. Удаление может повлиять на неё. Точно удалить?",
                        $"\"{node.DisplayName}\" was created by ANOTHER program, not this tool. Removing it may affect that program. Remove it anyway?");
            }
            else
            {
                question = node.IsGroup
                    ? Localization.T($"Удалить группу «{node.DisplayName}» со всеми пунктами внутри?", $"Remove the group \"{node.DisplayName}\" and everything inside it?")
                    : Localization.T($"Удалить «{node.DisplayName}» из меню?", $"Remove \"{node.DisplayName}\" from the menu?");
            }

            var result = MessageBox.Show(FindForm(), question, Localization.T("Удалить пункт меню", "Remove menu entry"),
                MessageBoxButtons.YesNo, node.IsOwned ? MessageBoxIcon.Question : MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                RunSafely(() => { _service.RemoveNode(node); return true; },
                    Localization.T("Не удалось удалить пункт меню.", "Couldn't remove the menu entry."));
                LoadEntries();
            }
        }

        /// <summary>"Protected" (HKLM) just means registered for every user of the machine, not necessarily
        /// a genuine Windows component — plenty of ordinary installers (MPC-HC, FastStone, etc.) do this.
        /// Deleting it needs admin rights, same mechanism as the Extensions tab's enable/disable.</summary>
        private async System.Threading.Tasks.Task RemoveProtectedNodeAsync(MenuNode node)
        {
            var question = node.IsGroup
                ? Localization.T(
                    $"«{node.DisplayName}» зарегистрирован в HKEY_LOCAL_MACHINE (для всех пользователей компьютера) — " +
                    "это не обязательно часть Windows, так делают инсталляторы многих обычных программ. Удаляется вся " +
                    "группа со всеми пунктами внутри. Нужны права администратора — запросить их сейчас?",
                    $"\"{node.DisplayName}\" is registered in HKEY_LOCAL_MACHINE (for every user of the machine) — " +
                    "that doesn't necessarily mean it's part of Windows, many ordinary installers do this. This removes " +
                    "the whole group with everything inside it. Administrator rights are needed — request them now?")
                : Localization.T(
                    $"«{node.DisplayName}» зарегистрирован в HKEY_LOCAL_MACHINE (для всех пользователей компьютера) — " +
                    "это не обязательно часть Windows, так делают инсталляторы многих обычных программ. Нужны права " +
                    "администратора — запросить их сейчас?",
                    $"\"{node.DisplayName}\" is registered in HKEY_LOCAL_MACHINE (for every user of the machine) — " +
                    "that doesn't necessarily mean it's part of Windows, many ordinary installers do this. Administrator " +
                    "rights are needed — request them now?");

            var confirm = MessageBox.Show(FindForm(), question, Localization.T("Нужны права администратора", "Administrator rights needed"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            Cursor.Current = Cursors.WaitCursor;
            bool ok;
            try { ok = await System.Threading.Tasks.Task.Run(() => RemoveProtectedNode(node)); }
            finally { Cursor.Current = Cursors.Default; }

            if (!ok)
            {
                MessageBox.Show(FindForm(),
                    Localization.T("Не удалось получить права администратора (запрос отклонён или произошла ошибка).",
                        "Couldn't get administrator rights (the request was declined or something went wrong)."),
                    Localization.T("Удалить пункт меню", "Remove menu entry"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LoadEntries();
        }

        private bool RemoveProtectedNode(MenuNode node)
        {
            try
            {
                _service.RemoveProtectedNodeDirect(node);
                return true;
            }
            catch (UnauthorizedAccessException) { /* fall through to elevation */ }
            catch (System.Security.SecurityException) { /* fall through to elevation */ }
            catch { return false; }

            return ElevatedRegistryClient.TryDeleteTree(node.SourceRootPath + "\\" + node.KeyName);
        }

        private void ExportEntries()
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                FileName = $"quick-launch-{_scope.ToString().ToLowerInvariant()}.json"
            })
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

                var tree = RunSafely(() => _service.GetMenuTree(), Localization.T("Не удалось прочитать меню.", "Couldn't read the menu."));
                if (tree == null) return;

                var links = MenuExportService.FlattenOwnedLinks(tree);
                var ok = RunSafely(() => { MenuExportService.Export(dialog.FileName, links); return true; },
                    Localization.T("Не удалось сохранить файл экспорта.", "Couldn't save the export file."));

                if (ok == true)
                {
                    Info(Localization.T($"Сохранено пунктов: {links.Count}", $"Entries saved: {links.Count}"));
                }
            }
        }

        private void ImportEntries()
        {
            using (var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json" })
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

                var imported = RunSafely(() => MenuExportService.Import(dialog.FileName), Localization.T("Не удалось прочитать файл импорта.", "Couldn't read the import file."));
                if (imported == null) return;

                if (imported.Count == 0)
                {
                    Info(Localization.T("В файле нет ни одного пункта.", "The file has no entries."));
                    return;
                }

                var result = MessageBox.Show(FindForm(),
                    Localization.T(
                        $"Импортировать {imported.Count} пункт(ов)? Пункты с совпадающими названиями (в той же группе) будут перезаписаны.",
                        $"Import {imported.Count} entr{(imported.Count == 1 ? "y" : "ies")}? Entries with matching names (in the same group) will be overwritten."),
                    Localization.T("Импорт", "Import"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result != DialogResult.Yes) return;

                RunSafely(() =>
                {
                    foreach (var entry in imported)
                    {
                        _service.AddOrUpdateLink(null, null, entry.DisplayName, entry.TargetPath, entry.Arguments, entry.IconPath, entry.GroupDisplayName);
                    }
                    return true;
                }, Localization.T("Не удалось импортировать один или несколько пунктов.", "Couldn't import one or more entries."));

                LoadEntries();
            }
        }

        private void Info(string message) =>
            MessageBox.Show(FindForm(), message, "WIN.right.CLICK", MessageBoxButtons.OK, MessageBoxIcon.Information);

        /// <summary>Runs a registry/IO operation, showing a friendly error dialog instead of crashing.</summary>
        private T? RunSafely<T>(Func<T> action, string errorMessage) where T : class
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), $"{errorMessage}\n\n{ex.Message}", Localization.T("Ошибка", "Error"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        private bool? RunSafely(Func<bool> action, string errorMessage)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), $"{errorMessage}\n\n{ex.Message}", Localization.T("Ошибка", "Error"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }
    }
}
