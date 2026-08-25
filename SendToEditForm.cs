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
                Text = "Изменить пункт «Отправить»";
                _saveButton.Text = "Сохранить";
                _nameBox.Text = existingEntry.DisplayName;
                _targetBox.Text = existingEntry.TargetPath;
                _argumentsBox.Text = existingEntry.Arguments;
                _iconBox.Text = existingEntry.IconPath;
            }
        }

        private void BuildUi()
        {
            Text = "Новый пункт «Отправить»";
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

            AddLabel(layout, "Название в меню «Отправить»");
            _nameBox = new TextBox { Dock = DockStyle.Fill };
            layout.Controls.Add(_nameBox); layout.SetColumnSpan(_nameBox, 2);

            AddLabel(layout, "Программа, которая получит выбранный файл/папку");
            _targetBox = new TextBox { Dock = DockStyle.Fill };
            var browseTargetButton = new Button { Text = "Обзор...", AutoSize = true };
            browseTargetButton.Click += (s, e) => BrowseTarget();
            layout.Controls.Add(_targetBox);
            layout.Controls.Add(browseTargetButton);

            AddLabel(layout, "Доп. аргументы перед путём к файлу (необязательно)");
            _argumentsBox = new TextBox { Dock = DockStyle.Fill };
            layout.Controls.Add(_argumentsBox); layout.SetColumnSpan(_argumentsBox, 2);

            AddLabel(layout, "Своя иконка (необязательно)");
            _iconBox = new TextBox { Dock = DockStyle.Fill };
            var browseIconButton = new Button { Text = "Обзор...", AutoSize = true };
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
                Filter = "Программы и ярлыки (*.exe;*.lnk;*.*)|*.exe;*.lnk;*.*",
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

        private void Save()
        {
            var name = _nameBox.Text.Trim();
            var target = _targetBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(this, "Укажи название пункта.", "Проверь данные", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(target))
            {
                MessageBox.Show(this, "Укажи программу.", "Проверь данные", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (Path.IsPathRooted(target) && !File.Exists(target) && !Directory.Exists(target))
            {
                MessageBox.Show(this, "Указанный путь не найден.", "Проверь данные", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var isRename = _existingEntry != null && !string.Equals(_existingEntry.DisplayName, name, StringComparison.OrdinalIgnoreCase);
            var isNewCollision = _existingEntry == null && _service.NameExists(name);

            if ((isRename || isNewCollision) && _service.NameExists(name))
            {
                var result = MessageBox.Show(this, $"Пункт «{name}» уже есть. Заменить его?", "Пункт уже существует",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result != DialogResult.Yes) return;
            }

            try
            {
                _service.AddOrUpdate(_existingEntry?.KeyName, name, target, _argumentsBox.Text.Trim(), _iconBox.Text.Trim());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Не удалось сохранить.\n\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
