using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using QuickLaunchMenuWinForms.Models;
using QuickLaunchMenuWinForms.Services;

namespace QuickLaunchMenuWinForms
{
    public class SendToEditForm : Form
    {
        private readonly SendToService _service;
        private readonly MenuEntry? _existingEntry;

        private TextBox _nameBox = null!;
        private TextBox _targetBox = null!;
        private TextBox _argumentsBox = null!;
        private TextBox _iconBox = null!;
        private Button _saveButton = null!;

        public SendToEditForm(SendToService service, MenuEntry? existingEntry)
        {
            _service = service;
            _existingEntry = existingEntry;
            BuildUi();

            if (existingEntry != null)
            {
                Text = Localization.T("Изменить пункт «Отправить»", "Edit \"Send To\" entry");
                _saveButton.Text = Localization.T("Сохранить", "Save");
                _nameBox.Text = existingEntry.DisplayName;
                _targetBox.Text = existingEntry.TargetPath;
                _argumentsBox.Text = existingEntry.Arguments;
                _iconBox.Text = existingEntry.IconPath;
            }
        }

        private void BuildUi()
        {
            Text = Localization.T("Новый пункт «Отправить»", "New \"Send To\" entry");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Width = 480;
            Height = 320;
            Font = new Font("Segoe UI", 9F);
            Padding = new Padding(16);

            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { /* keep default icon */ }

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 8,
                AutoSize = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AddLabel(layout, Localization.T("Название в меню «Отправить»", "Name in the \"Send To\" menu"));
            _nameBox = new TextBox { Dock = DockStyle.Fill };
            layout.Controls.Add(_nameBox); layout.SetColumnSpan(_nameBox, 2);

            AddLabel(layout, Localization.T("Программа, которая получит выбранный файл/папку", "Program that will receive the selected file/folder"));
            _targetBox = new TextBox { Dock = DockStyle.Fill };
            var browseTargetButton = new Button { Text = Localization.T("Обзор...", "Browse..."), AutoSize = true };
            browseTargetButton.Click += (s, e) => BrowseTarget();
            layout.Controls.Add(_targetBox);
            layout.Controls.Add(browseTargetButton);

            AddLabel(layout, Localization.T("Доп. аргументы перед путём к файлу (необязательно)", "Extra arguments before the file path (optional)"));
            _argumentsBox = new TextBox { Dock = DockStyle.Fill };
            layout.Controls.Add(_argumentsBox); layout.SetColumnSpan(_argumentsBox, 2);

            AddLabel(layout, Localization.T("Своя иконка (необязательно)", "Custom icon (optional)"));
            _iconBox = new TextBox { Dock = DockStyle.Fill };
            var browseIconButton = new Button { Text = Localization.T("Обзор...", "Browse..."), AutoSize = true };
            browseIconButton.Click += (s, e) => BrowseIcon();
            layout.Controls.Add(_iconBox);
            layout.Controls.Add(browseIconButton);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 44,
                Padding = new Padding(0, 8, 0, 0)
            };

            _saveButton = new Button { Text = Localization.T("Добавить", "Add"), AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            var cancelButton = new Button { Text = Localization.T("Отмена", "Cancel"), AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
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

        private static void AddLabel(TableLayoutPanel layout, string text)
        {
            var label = new Label { Text = text, AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
            layout.Controls.Add(label);
            layout.SetColumnSpan(label, 2);
        }

        private void BrowseTarget()
        {
            using (var dialog = new OpenFileDialog
            {
                Filter = Localization.T("Программы и ярлыки (*.exe;*.lnk;*.*)|*.exe;*.lnk;*.*", "Programs and shortcuts (*.exe;*.lnk;*.*)|*.exe;*.lnk;*.*"),
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
                Filter = Localization.T("Иконки и программы (*.ico;*.exe;*.dll)|*.ico;*.exe;*.dll|Все файлы (*.*)|*.*", "Icons and programs (*.ico;*.exe;*.dll)|*.ico;*.exe;*.dll|All files (*.*)|*.*"),
                CheckFileExists = true
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _iconBox.Text = dialog.FileName;
                }
            }
        }

        private void Save()
        {
            var name = _nameBox.Text.Trim();
            var target = _targetBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this, Localization.T("Укажи название пункта.", "Enter a name for the entry."),
                    Localization.T("Проверь данные", "Check the details"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(target))
            {
                MessageBox.Show(this, Localization.T("Укажи программу.", "Enter a program."),
                    Localization.T("Проверь данные", "Check the details"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (Path.IsPathRooted(target) && !File.Exists(target) && !Directory.Exists(target))
            {
                MessageBox.Show(this, Localization.T("Указанный путь не найден.", "That path wasn't found."),
                    Localization.T("Проверь данные", "Check the details"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var isRename = _existingEntry != null && !string.Equals(_existingEntry.DisplayName, name, StringComparison.OrdinalIgnoreCase);
            var isNewCollision = _existingEntry == null && _service.NameExists(name);

            if ((isRename || isNewCollision) && _service.NameExists(name))
            {
                var result = MessageBox.Show(this,
                    Localization.T($"Пункт «{name}» уже есть. Заменить его?", $"An entry \"{name}\" already exists. Replace it?"),
                    Localization.T("Пункт уже существует", "Entry already exists"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result != DialogResult.Yes) return;
            }

            try
            {
                _service.AddOrUpdate(_existingEntry?.KeyName, name, target, _argumentsBox.Text.Trim(), _iconBox.Text.Trim());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Localization.T($"Не удалось сохранить.\n\n{ex.Message}", $"Couldn't save.\n\n{ex.Message}"),
                    Localization.T("Ошибка", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
