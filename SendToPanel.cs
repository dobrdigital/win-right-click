using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using QuickLaunchMenuWinForms.Models;
using QuickLaunchMenuWinForms.Services;

namespace QuickLaunchMenuWinForms
{
    /// <summary>Manages the "Send To" submenu — a folder of .lnk shortcuts, not a registry key.
    /// Live-refreshes via FileSystemWatcher instead of the registry-notify trick the other tabs use.</summary>
    public class SendToPanel : UserControl
    {
        private readonly SendToService _service = new SendToService();
        private readonly ImageList _icons = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };

        private ListView _listView = null!;
        private Label _emptyLabel = null!;
        private FileSystemWatcher? _watcher;

        public SendToPanel()
        {
            BuildUi();
            LoadEntries();
            StartWatcher();
            Disposed += (s, e) => _watcher?.Dispose();
        }

        private void BuildUi()
        {
            Dock = DockStyle.Fill;
            Font = new Font("Segoe UI", 9F);

            var headerPanel = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(16, 10, 16, 4) };
            var titleLabel = new Label
            {
                Text = "Меню «Отправить» (Send To)",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 22
            };
            var subtitleLabel = new Label
            {
                Text = $"Не реестр — просто ярлыки в {_service.FolderPath}. Программа получает выбранный файл автоматически.",
                ForeColor = SystemColors.GrayText,
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 20
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
                SmallImageList = _icons
            };
            _listView.Columns.Add("Название", 220);
            _listView.Columns.Add("Программа", 380);
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
                Text = "В меню «Отправить» пока пусто.",
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

            var addButton = new Button { Text = "Добавить...", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            var editButton = new Button { Text = "Изменить", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            var removeButton = new Button { Text = "Удалить", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            var refreshButton = new Button { Text = "Обновить", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            var openFolderButton = new Button { Text = "Открыть папку", AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(24, 3, 3, 3) };

            addButton.Click += (s, e) => AddNew();
            editButton.Click += (s, e) => EditSelected();
            removeButton.Click += (s, e) => RemoveSelected();
            refreshButton.Click += (s, e) => LoadEntries();
            openFolderButton.Click += (s, e) => OpenFolder();

            buttonPanel.Controls.Add(addButton);
            buttonPanel.Controls.Add(editButton);
            buttonPanel.Controls.Add(removeButton);
            buttonPanel.Controls.Add(refreshButton);
            buttonPanel.Controls.Add(openFolderButton);

            Controls.Add(listContainer);
            Controls.Add(buttonPanel);
            Controls.Add(headerPanel);
        }

        private void StartWatcher()
        {
            try
            {
                Directory.CreateDirectory(_service.FolderPath);
                _watcher = new FileSystemWatcher(_service.FolderPath, "*.lnk")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                _watcher.Created += (s, e) => SafeReload();
                _watcher.Deleted += (s, e) => SafeReload();
                _watcher.Renamed += (s, e) => SafeReload();
                _watcher.Changed += (s, e) => SafeReload();
            }
            catch
            {
                // Live refresh is a convenience — the Refresh button still works if the watcher can't start.
            }
        }

        private void SafeReload()
        {
            if (IsDisposed) return;
            try
            {
                if (IsHandleCreated) BeginInvoke(new Action(LoadEntries));
            }
            catch
            {
                // Ignore — form may be closing.
            }
        }

        private void LoadEntries()
        {
            if (IsDisposed) return;

            _listView.Items.Clear();
            _icons.Images.Clear();

            var entries = RunSafely(() => _service.GetEntries(), "Не удалось прочитать папку «Отправить».");
            if (entries == null) return;

            foreach (var entry in entries)
            {
                var item = new ListViewItem(entry.DisplayName) { Tag = entry };
                item.SubItems.Add(entry.TargetPath);
                item.SubItems.Add(entry.IsSpecial ? string.Empty : "⋯");
                item.SubItems.Add("?");
                if (entry.IsSpecial) item.ForeColor = SystemColors.GrayText;

                var iconSourcePath = entry.IsSpecial ? null
                    : (string.IsNullOrWhiteSpace(entry.IconPath) ? entry.TargetPath : entry.IconPath);
                if (!string.IsNullOrWhiteSpace(iconSourcePath))
                {
                    if (!_icons.Images.ContainsKey(iconSourcePath))
                    {
                        var icon = IconHelper.GetIconFor(iconSourcePath!);
                        if (icon != null) _icons.Images.Add(iconSourcePath, icon);
                    }
                    if (_icons.Images.ContainsKey(iconSourcePath)) item.ImageKey = iconSourcePath;
                }

                _listView.Items.Add(item);
            }

            var isEmpty = _listView.Items.Count == 0;
            _emptyLabel.Visible = isEmpty;
            _listView.Visible = !isEmpty;
            Theme.RefreshColumnLayout(_listView);
        }

        private MenuEntry? SelectedEntry =>
            _listView.SelectedItems.Count > 0 ? _listView.SelectedItems[0].Tag as MenuEntry : null;

        /// <summary>The "⋯" cell (second-to-last column, added in BuildUi) opens the folder containing the
        /// shortcut's target and highlights the file itself.</summary>
        private void OpenContainingFolderIfActionButtonClicked(MouseEventArgs e)
        {
            var hit = _listView.HitTest(e.Location);
            if (hit.Item == null) return;

            var actionColumnIndex = _listView.Columns.Count - 2;
            if (actionColumnIndex < 1 || actionColumnIndex >= hit.Item.SubItems.Count) return;
            if (!hit.Item.SubItems[actionColumnIndex].Bounds.Contains(e.Location)) return;

            var entry = hit.Item.Tag as MenuEntry;
            var path = entry?.TargetPath;
            if (entry == null || entry.IsSpecial || string.IsNullOrWhiteSpace(path))
            {
                Info("У этого пункта нет пути к файлу.");
                return;
            }

            var resolved = PathResolver.ResolveExisting(path!);
            if (resolved == null)
            {
                Info("Файл или папка не найдены — ни по указанному пути, ни в PATH.");
                return;
            }

            try
            {
                if (Directory.Exists(resolved)) Process.Start("explorer.exe", $"\"{resolved}\"");
                else Process.Start("explorer.exe", $"/select,\"{resolved}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), $"Не удалось открыть папку.\n\n{ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>The "?" cell (last column, added in BuildUi) looks up this entry online.</summary>
        private void SearchOnlineIfActionButtonClicked(MouseEventArgs e)
        {
            var hit = _listView.HitTest(e.Location);
            if (hit.Item == null) return;

            var actionColumnIndex = _listView.Columns.Count - 1;
            if (actionColumnIndex < 1 || actionColumnIndex >= hit.Item.SubItems.Count) return;
            if (!hit.Item.SubItems[actionColumnIndex].Bounds.Contains(e.Location)) return;

            var entry = hit.Item.Tag as MenuEntry;
            if (entry == null) return;

            MenuManagerPanel.SearchOnline(entry.DisplayName, entry.IsSpecial ? null : entry.TargetPath);
        }

        private void AddNew()
        {
            using (var dialog = new SendToEditForm(_service, null))
            {
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK) LoadEntries();
            }
        }

        private void EditSelected()
        {
            var entry = SelectedEntry;
            if (entry == null) { Info("Сначала выбери пункт в списке."); return; }

            if (entry.IsSpecial)
            {
                Info("Это системный пункт Windows (не обычный ярлык) — его нельзя редактировать, только удалить.");
                return;
            }

            using (var dialog = new SendToEditForm(_service, entry))
            {
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK) LoadEntries();
            }
        }

        private void RemoveSelected()
        {
            var entry = SelectedEntry;
            if (entry == null) { Info("Сначала выбери пункт в списке."); return; }

            var result = MessageBox.Show(FindForm(), $"Удалить «{entry.DisplayName}» из меню «Отправить»?",
                "Удалить пункт", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                RunSafely(() => { _service.Remove(entry.KeyName); return true; }, "Не удалось удалить пункт.");
                LoadEntries();
            }
        }

        private void OpenFolder()
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"\"{_service.FolderPath}\""); }
            catch { /* non-fatal */ }
        }

        private void Info(string message) =>
            MessageBox.Show(FindForm(), message, "Отправить", MessageBoxButtons.OK, MessageBoxIcon.Information);

        private T? RunSafely<T>(Func<T> action, string errorMessage) where T : class
        {
            try { return action(); }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), $"{errorMessage}\n\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        private bool? RunSafely(Func<bool> action, string errorMessage)
        {
            try { return action(); }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), $"{errorMessage}\n\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }
    }
}
