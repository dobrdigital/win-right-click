using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using QuickLaunchMenuWinForms.Models;
using QuickLaunchMenuWinForms.Services;

namespace QuickLaunchMenuWinForms
{
    public class MainForm : Form
    {
        public MainForm()
        {
            Text = "WIN.right.CLICK 0.99 beta";
            Width = 1020;
            Height = 620;
            MinimumSize = new Size(820, 460);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                // Keep the default icon if extraction fails.
            }

            var topBar = new Panel { Dock = DockStyle.Top, Height = 68, Padding = new Padding(16, 0, 16, 0) };
            var appTitle = new Label
            {
                Text = "WIN.right.CLICK  0.99 beta",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 18)
            };

            var langPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 84,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 18, 8, 0)
            };
            var ruButton = new Button { Text = "RU", Width = 36, Height = 30, Margin = new Padding(0, 0, 2, 0) };
            var enButton = new Button { Text = "EN", Width = 36, Height = 30, Margin = new Padding(0) };
            ruButton.Font = new Font(ruButton.Font, Localization.Current == AppLanguage.Ru ? FontStyle.Bold : FontStyle.Regular);
            enButton.Font = new Font(enButton.Font, Localization.Current == AppLanguage.En ? FontStyle.Bold : FontStyle.Regular);
            ruButton.Click += (s, e) => SwitchLanguage(AppLanguage.Ru);
            enButton.Click += (s, e) => SwitchLanguage(AppLanguage.En);
            langPanel.Controls.Add(ruButton);
            langPanel.Controls.Add(enButton);

            var logo = new LogoPanel { Dock = DockStyle.Right, Margin = new Padding(0, 4, 0, 4) };
            var logoHost = new Panel { Dock = DockStyle.Right, Width = 240, Padding = new Padding(0, 4, 0, 4) };
            logo.Dock = DockStyle.Fill;
            logoHost.Controls.Add(logo);
            topBar.Controls.Add(appTitle);
            topBar.Controls.Add(logoHost);
            topBar.Controls.Add(langPanel);

            var tabs = new FlatTabControl { Dock = DockStyle.Fill };

            var lockExplanation = " 🔒 " + Localization.T(
                "«Общий (HKLM)» — не значит «системный»: так помечен любой пункт, записанный для всех " +
                "пользователей компьютера, а не только текущего. Так делают инсталляторы многих обычных " +
                "программ — изменить/удалить такой пункт отсюда нельзя без прав администратора.",
                "\"Common (HKLM)\" doesn't mean \"system\": it just marks anything registered for every user " +
                "of the machine, not only the current one — that's what installers for many ordinary programs " +
                "do. Changing/deleting it from here needs administrator rights.");

            var desktopPage = new TabPage(Localization.T("Рабочий стол", "Desktop"));
            desktopPage.Controls.Add(new MenuManagerPanel(
                MenuScope.Desktop,
                Localization.T("Меню правого клика на рабочем столе", "Right-click menu on the desktop"),
                Localization.T("Показаны все пункты, включая чужие программы.", "Shows every entry, including other programs'.") + lockExplanation));

            var folderPage = new TabPage(Localization.T("Папки", "Folders"));
            folderPage.Controls.Add(new MenuManagerPanel(
                MenuScope.Folder,
                Localization.T("Меню правого клика НА папке", "Right-click menu ON a folder"),
                Localization.T("Программа получает путь к кликнутой папке автоматически.", "The program automatically gets the path to the clicked folder.") + lockExplanation));

            var filePage = new TabPage(Localization.T("Файлы", "Files"));
            filePage.Controls.Add(new MenuManagerPanel(
                MenuScope.File,
                Localization.T("Меню правого клика НА любом файле", "Right-click menu ON any file"),
                Localization.T("Программа получает путь к кликнутому файлу автоматически.", "The program automatically gets the path to the clicked file.") + lockExplanation));

            var sendToPage = new TabPage(Localization.T("Отправить", "Send To"));
            sendToPage.Controls.Add(new SendToPanel());

            var extensionsPage = new TabPage(Localization.T("Расширения", "Extensions"));
            extensionsPage.Controls.Add(new ExtensionsPanel());

            tabs.TabPages.Add(desktopPage);
            tabs.TabPages.Add(folderPage);
            tabs.TabPages.Add(filePage);
            tabs.TabPages.Add(sendToPage);
            tabs.TabPages.Add(extensionsPage);

            Controls.Add(tabs);
            Controls.Add(topBar);

            Theme.Apply(this);
        }

        private static void SwitchLanguage(AppLanguage lang)
        {
            if (Localization.Current == lang) return;
            Localization.Save(lang);
            try { Process.Start(Application.ExecutablePath); }
            catch { /* worst case the user restarts manually */ }
            Application.Exit();
        }
    }
}
