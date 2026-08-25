using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using QuickLaunchMenuWinForms.Models;
using QuickLaunchMenuWinForms.Services;

namespace QuickLaunchMenuWinForms
{
    /// <summary>
    /// One consolidated, searchable list of every COM shell extension (shellex\ContextMenuHandlers) found
    /// across Desktop/Folder/File contexts — pulled out of the per-scope tabs so entries like "Give access
    /// to" or "Include in library" (whose registry key names don't obviously match their menu text — the
    /// COM object decides its own text at runtime) are easy to find instead of buried in a long mixed list.
    /// </summary>
    public class ExtensionsPanel : UserControl
    {
        private readonly ContextMenuService _desktopService = new ContextMenuService(MenuScope.Desktop);
        private readonly ContextMenuService _folderService = new ContextMenuService(MenuScope.Folder);
        private readonly ContextMenuService _fileService = new ContextMenuService(MenuScope.File);

        private ListView _listView = null!;
        private TextBox _searchBox = null!;
        private Label _emptyLabel = null!;
        private List<MenuNode> _all = new List<MenuNode>();

        public ExtensionsPanel()
        {
            BuildUi();
            LoadEntries();
        }

        private void BuildUi()
        {
            Dock = DockStyle.Fill;
            Font = new Font("Segoe UI", 9F);

            var headerPanel = new Panel { Dock = DockStyle.Top, Height = 118, Padding = new Padding(16, 10, 16, 4) };
            var titleLabel = new Label
            {
                Text = "Расширения контекстного меню (COM)",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 22
            };
            var subtitleLabel = new Label
            {
                Text = "Такие как 7-Zip, «Предоставить доступ», «Включить в библиотеку», антивирус. Их код сам решает, " +
                       "какой текст показать в меню — точное название не всегда угадывается из реестра, поэтому ниже указан " +
                       "и технический компонент. Можно включать/выключать (обратимо), нельзя удалить.",
                ForeColor = SystemColors.GrayText,
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 40
            };
            _searchBox = new TextBox { Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 0) };
            _searchBox.TextChanged += (s, e) => ApplyFilter();
            var searchLabel = new Label { Text = "Поиск:", Dock = DockStyle.Top, Height = 18, ForeColor = SystemColors.GrayText };

            headerPanel.Controls.Add(_searchBox);
            headerPanel.Controls.Add(searchLabel);
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
                ShowGroups = true
            };
            _listView.Columns.Add("Название", 320);
            _listView.Columns.Add("Компонент (DLL)", 280);
            _listView.Columns.Add("Где действует", 160);
            _listView.Columns.Add("Статус", 110);
            _listView.DoubleClick += async (s, e) => await ToggleSelectedAsync();

            _emptyLabel = new Label
            {
                Text = "Ничего не найдено.",
                ForeColor = SystemColors.GrayText,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Visible = false
            };

            var listContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 4) };
            listContainer.Controls.Add(_listView);
            listContainer.Controls.Add(_emptyLabel);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(16, 8, 16, 8)
            };
            var toggleButton = new Button { Text = "Вкл/Выкл", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            var refreshButton = new Button { Text = "Обновить", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            toggleButton.Click += async (s, e) => await ToggleSelectedAsync();
            refreshButton.Click += (s, e) => LoadEntries();
            buttonPanel.Controls.Add(toggleButton);
            buttonPanel.Controls.Add(refreshButton);

            Controls.Add(listContainer);
            Controls.Add(buttonPanel);
            Controls.Add(headerPanel);
        }

        private void LoadEntries()
        {
            var all = new List<MenuNode>();
            foreach (var svc in new[] { _desktopService, _folderService, _fileService })
            {
                try { all.AddRange(svc.GetExtensions()); }
                catch (Exception ex)
                {
                    MessageBox.Show(FindForm(), $"Не удалось прочитать часть расширений.\n\n{ex.Message}",
                        "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            // The same handler is often registered for more than one context (e.g. Directory AND
            // AllFilesystemObjects) — collapse identical (name + DLL + hive) rows into one, merging
            // the "where" label so it's still clear it applies broadly.
            _all = all
                .GroupBy(n => (n.DisplayName, n.Extension?.DllPath, n.Extension?.IsHklm))
                .Select(g =>
                {
                    var first = g.First();
                    if (g.Count() > 1)
                    {
                        first.SourceLabel = string.Join(" + ", g.Select(x => x.SourceLabel).Distinct());
                    }
                    return first;
                })
                .OrderBy(n => n.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            _listView.Items.Clear();
            _listView.Groups.Clear();

            var query = _searchBox.Text.Trim();
            var filtered = string.IsNullOrEmpty(query)
                ? _all
                : _all.Where(n =>
                    n.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.KeyName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (n.Extension?.DllPath?.IndexOf(query, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    .ToList();

            var enabledGroup = new ListViewGroup("Включено");
            var disabledGroup = new ListViewGroup("Отключено");
            _listView.Groups.Add(enabledGroup);
            _listView.Groups.Add(disabledGroup);

            foreach (var node in filtered)
            {
                var ext = node.Extension!;
                var item = new ListViewItem(node.DisplayName) { Tag = node };
                item.SubItems.Add(System.IO.Path.GetFileName(ext.DllPath) ?? string.Empty);
                item.SubItems.Add(node.SourceLabel + (ext.IsHklm ? " (систем.)" : " (польз.)"));
                item.SubItems.Add(ext.IsDisabled ? "Выключено" : "Включено");
                item.Group = ext.IsDisabled ? disabledGroup : enabledGroup;
                if (ext.IsDisabled) item.ForeColor = SystemColors.GrayText;
                _listView.Items.Add(item);
            }

            var isEmpty = _listView.Items.Count == 0;
            _emptyLabel.Visible = isEmpty;
            _listView.Visible = !isEmpty;
            Theme.RefreshColumnLayout(_listView);
        }

        private async System.Threading.Tasks.Task ToggleSelectedAsync()
        {
            if (_listView.SelectedItems.Count == 0)
            {
                MessageBox.Show(FindForm(), "Сначала выбери расширение в списке.", "Расширения",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var node = (MenuNode)_listView.SelectedItems[0].Tag;
            var ext = node.Extension!;
            var willEnable = ext.IsDisabled;
            var actionWord = willEnable ? "включить" : "отключить";

            var confirm = MessageBox.Show(FindForm(),
                $"{(willEnable ? "Включить" : "Отключить")} «{node.DisplayName}»?\n\n" +
                "Это не удаляет и не переустанавливает программу — только переключает регистрацию пункта меню " +
                "(тот же способ, что использует ShellExView). Полностью обратимо.",
                "Расширение", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            // Whichever scope's service instance found this node owns the write — any of the three works,
            // since SetExtensionEnabled only needs the handler's relative sub-path (already scope-agnostic).
            try
            {
                _desktopService.SetExtensionEnabled(ext, willEnable);
                LoadEntries();
                MessageBox.Show(FindForm(),
                    $"Готово: «{node.DisplayName}» {(willEnable ? "включено" : "отключено")}.\n\nЕсли не подействует сразу — перезапусти проводник (Explorer) или компьютер.",
                    "WIN.right.CLICK", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            catch (UnauthorizedAccessException) { /* fall through to elevation */ }
            catch (System.Security.SecurityException) { /* fall through to elevation */ }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), $"Не удалось изменить расширение.\n\n{ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!ext.IsHklm)
            {
                MessageBox.Show(FindForm(), "Недостаточно прав, чтобы это изменить.", "Расширения",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var elevate = MessageBox.Show(FindForm(),
                $"Чтобы {actionWord} это расширение, нужны права администратора. Запросить их сейчас?\n\n" +
                "Если это первое такое действие в текущем запуске программы — Windows покажет запрос " +
                "администратора один раз; для дальнейших переключений других расширений он больше не появится.",
                "Нужны права администратора", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (elevate != DialogResult.Yes) return;

            var newValue = willEnable ? ext.Clsid : "-" + ext.Clsid;
            var subPath = @"Software\Classes\" + ext.HandlerSubPath;

            // Starting the elevated helper (first time) and talking to it over the pipe can take a moment
            // (UAC prompt, .NET process cold start) — do it off the UI thread so the window doesn't look
            // frozen while it waits.
            Cursor.Current = Cursors.WaitCursor;
            bool ok;
            try { ok = await System.Threading.Tasks.Task.Run(() => ElevatedRegistryClient.TrySetValue(subPath, newValue)); }
            finally { Cursor.Current = Cursors.Default; }

            if (!ok)
            {
                MessageBox.Show(FindForm(), "Не удалось получить права администратора (запрос отклонён или произошла ошибка).",
                    "Расширения", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LoadEntries();
        }
    }
}
