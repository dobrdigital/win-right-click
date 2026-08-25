using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using QuickLaunchMenuWinForms.Models;
using QuickLaunchMenuWinForms.Services;

namespace QuickLaunchMenuWinForms
{
    public class AddEditForm : Form
    {
        private readonly ContextMenuService _service;
        private readonly MenuScope _scope;
        private readonly MenuEntry? _existingEntry;
        private readonly MenuNode? _existingNode;

        private TextBox _nameBox = null!;
        private TextBox _targetBox = null!;
        private TextBox _argumentsBox = null!;
        private TextBox _iconBox = null!;
        private ComboBox _groupBox = null!;
        private Button _saveButton = null!;

        /// <param name="service">The scope-specific service to save into (Desktop/Folder/File).</param>
        /// <param name="scope">Same scope as <paramref name="service"/> — used only to adjust labels/hints.</param>
        /// <param name="existingEntry">Pass an existing link to edit it, or null to create a new one.</param>
        /// <param name="existingNode">The MenuNode existingEntry came from — only needed when it's a
        /// "protected" (HKLM) entry, so editing knows to write via elevation instead of HKCU.</param>
        /// <param name="prefillTargetPath">Pre-fill the target (used for the "add from file/folder right-click" flow).</param>
        /// <param name="standalone">True when this form is the only window of the process (launched via --add).</param>
        public AddEditForm(ContextMenuService service, MenuScope scope, MenuEntry? existingEntry,
            MenuNode? existingNode = null, string? prefillTargetPath = null, bool standalone = false)
        {
            _service = service;
            _scope = scope;
            _existingEntry = existingEntry;
            _existingNode = existingNode;
            BuildUi();
            LoadGroups();

            if (existingEntry != null)
            {
                Text = "Изменить пункт меню";
                _saveButton.Text = "Сохранить";
                _nameBox.Text = existingEntry.DisplayName;
                _targetBox.Text = existingEntry.TargetPath;
                _argumentsBox.Text = existingEntry.Arguments;
                _iconBox.Text = existingEntry.IconPath;
                _groupBox.Text = existingEntry.GroupDisplayName ?? string.Empty;

                if (_existingNode != null && _existingNode.IsProtected)
                {
                    // Protected entries keep their existing registry key as-is (a foreign program's key
                    // name has nothing to do with its display name) — group/rename-driven key moves aren't
                    // supported for them, so there's nothing meaningful for this field to do.
                    _groupBox.Enabled = false;
                }
            }
            else if (!string.IsNullOrWhiteSpace(prefillTargetPath))
            {
                _targetBox.Text = prefillTargetPath;
                _nameBox.Text = Directory.Exists(prefillTargetPath)
                    ? new DirectoryInfo(prefillTargetPath).Name
                    : Path.GetFileNameWithoutExtension(prefillTargetPath);
            }

            if (standalone)
            {
                ShowInTaskbar = true;
                StartPosition = FormStartPosition.CenterScreen;
            }
        }

        private void BuildUi()
        {
            Text = "Новый пункт меню";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Width = 480;
            Height = _scope == MenuScope.Desktop ? 380 : 400;
            Font = new Font("Segoe UI", 9F);
            Padding = new Padding(16);

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                // Keep default icon if extraction fails.
            }

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 10,
                AutoSize = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AddLabel(layout, "Название в меню", 0, span: true);
            _nameBox = new TextBox { Dock = DockStyle.Fill };
            layout.Controls.Add(_nameBox, 0, 1);
            layout.SetColumnSpan(_nameBox, 2);

            var targetLabelText = _scope == MenuScope.Desktop
                ? "Программа, файл или папка"
                : "Программа, которая откроет " + (_scope == MenuScope.Folder ? "папку" : "файл");
            AddLabel(layout, targetLabelText, 0, 2, span: true);
            _targetBox = new TextBox { Dock = DockStyle.Fill };
            var browseTargetButton = new Button { Text = "Обзор...", AutoSize = true };
            browseTargetButton.Click += (s, e) => BrowseTarget();
            layout.Controls.Add(_targetBox, 0, 3);
            layout.Controls.Add(browseTargetButton, 1, 3);

            var argumentsHint = _service.UsesClickedItemPlaceholder
                ? "Аргументы (необязательно — путь к кликнутому объекту подставится сам)"
                : "Аргументы командной строки (необязательно)";
            AddLabel(layout, argumentsHint, 0, 4, span: true);
            _argumentsBox = new TextBox { Dock = DockStyle.Fill };
            layout.Controls.Add(_argumentsBox, 0, 5);
            layout.SetColumnSpan(_argumentsBox, 2);

            AddLabel(layout, "Своя иконка (необязательно)", 0, 6, span: true);
            _iconBox = new TextBox { Dock = DockStyle.Fill };
            var browseIconButton = new Button { Text = "Обзор...", AutoSize = true };
            browseIconButton.Click += (s, e) => BrowseIcon();
            layout.Controls.Add(_iconBox, 0, 7);
            layout.Controls.Add(browseIconButton, 1, 7);

            AddLabel(layout, "Группа/подменю (необязательно — оставь пустым для прямой ссылки)", 0, 8, span: true);
            _groupBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
            layout.Controls.Add(_groupBox, 0, 9);
            layout.SetColumnSpan(_groupBox, 2);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 44,
                Padding = new Padding(0, 8, 0, 0)
            };

            _saveButton = new Button { Text = "Добавить", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            var cancelButton = new Button { Text = "Отмена", AutoSize = true, Padding = new Padding(8, 4, 8, 4) };

            _saveButton.Click += (s, e) => Save();
            cancelButton.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            buttonPanel.Controls.Add(_saveButton);
            buttonPanel.Controls.Add(cancelButton);

            AcceptButton = _saveButton;
            CancelButton = cancelButton;

            var contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
            contentPanel.Controls.Add(layout);

            Controls.Add(contentPanel);
            Controls.Add(buttonPanel);

            Theme.Apply(this);
        }

        private static void AddLabel(TableLayoutPanel layout, string text, int col, int row = -1, bool span = false)
        {
            var label = new Label { Text = text, AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
            if (row >= 0) layout.Controls.Add(label, col, row);
            else layout.Controls.Add(label);
            if (span) layout.SetColumnSpan(label, 2);
        }

        private void LoadGroups()
        {
            try
            {
                foreach (var name in _service.GetOwnedGroupNames())
                {
                    _groupBox.Items.Add(name);
                }
            }
            catch
            {
                // Non-fatal — the combo just stays empty/free-text.
            }
        }

        private void BrowseTarget()
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "Программы, ярлыки и файлы (*.exe;*.lnk;*.*)|*.exe;*.lnk;*.*",
                CheckFileExists = true
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _targetBox.Text = dialog.FileName;
                    if (string.IsNullOrWhiteSpace(_nameBox.Text))
                    {
                        _nameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
                    }
                }
            }
        }

        private void BrowseIcon()
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = "Иконки и программы (*.ico;*.exe;*.dll)|*.ico;*.exe;*.dll|Все файлы (*.*)|*.*",
                CheckFileExists = true
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _iconBox.Text = dialog.FileName;
                }
            }
        }

        private async void Save()
        {
            var name = _nameBox.Text.Trim();
            var target = _targetBox.Text.Trim();
            var group = _groupBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this, "Укажи название пункта меню.", "Проверь данные",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                MessageBox.Show(this, "Укажи программу, файл или папку для запуска.", "Проверь данные",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (Path.IsPathRooted(target) && !File.Exists(target) && !Directory.Exists(target))
            {
                MessageBox.Show(this, "Указанный путь не найден. Проверь его или используй кнопку «Обзор...».",
                    "Проверь данные", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_existingNode != null && _existingNode.IsProtected)
            {
                await SaveProtectedAsync(name, target, _argumentsBox.Text.Trim(), _iconBox.Text.Trim());
                return;
            }

            var newGroup = string.IsNullOrWhiteSpace(group) ? null : group;
            var newKeyName = _service.BuildLinkKeyName(name);
            var existingGroup = _existingEntry?.GroupDisplayName;
            var isRenameOrMove = _existingEntry != null &&
                (!string.Equals(_existingEntry.KeyName, newKeyName, StringComparison.Ordinal) ||
                 !string.Equals(existingGroup ?? string.Empty, newGroup ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            var isNewCollision = _existingEntry == null && _service.KeyNameExists(newKeyName, newGroup);

            if ((isRenameOrMove || isNewCollision) && _service.KeyNameExists(newKeyName, newGroup))
            {
                var result = MessageBox.Show(this,
                    $"Пункт с названием «{name}» уже есть {(newGroup == null ? "среди прямых ссылок" : $"в группе «{newGroup}»")}. Заменить его?",
                    "Пункт уже существует", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result != DialogResult.Yes)
                {
                    return;
                }
            }

            try
            {
                _service.AddOrUpdateLink(
                    existingKeyName: _existingEntry?.KeyName,
                    existingGroupDisplayName: existingGroup,
                    displayName: name,
                    targetPath: target,
                    arguments: _argumentsBox.Text.Trim(),
                    iconPath: _iconBox.Text.Trim(),
                    newGroupDisplayName: newGroup);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Не удалось сохранить пункт меню.\n\n{ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Updates an existing HKLM ("protected") link's target/icon/display name in place —
        /// same registry key, no rename or group move. Needs admin rights, same mechanism as the
        /// Extensions tab's enable/disable and the table's "delete a protected entry" action.</summary>
        private async System.Threading.Tasks.Task SaveProtectedAsync(string name, string target, string arguments, string icon)
        {
            var confirm = MessageBox.Show(this,
                "Это пункт из HKEY_LOCAL_MACHINE (для всех пользователей компьютера) — не обязательно часть " +
                "Windows, так делают инсталляторы многих обычных программ. Изменение потребует прав " +
                "администратора. Продолжить?",
                "Нужны права администратора", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            var writes = _service.BuildProtectedLinkUpdate(_existingNode!, name, target, arguments,
                string.IsNullOrWhiteSpace(icon) ? null : icon);

            Cursor.Current = Cursors.WaitCursor;
            bool ok;
            try { ok = await System.Threading.Tasks.Task.Run(() => ApplyProtectedWrites(writes)); }
            finally { Cursor.Current = Cursors.Default; }

            if (!ok)
            {
                MessageBox.Show(this, "Не удалось получить права администратора (запрос отклонён или произошла ошибка).",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool ApplyProtectedWrites(System.Collections.Generic.List<ElevatedRegistryWrite> writes)
        {
            try
            {
                foreach (var write in writes)
                {
                    using (var key = Registry.LocalMachine.CreateSubKey(write.Path, writable: true))
                    {
                        if (key == null) throw new UnauthorizedAccessException("Нет доступа на запись в HKEY_LOCAL_MACHINE.");
                        key.SetValue(write.Name, write.Value);
                    }
                }
                return true;
            }
            catch (UnauthorizedAccessException) { /* fall through to elevation */ }
            catch (System.Security.SecurityException) { /* fall through to elevation */ }
            catch { return false; }

            return ElevatedRegistryClient.TrySetValues(writes);
        }
    }
}
