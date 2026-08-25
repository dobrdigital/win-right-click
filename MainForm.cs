using System.Drawing;
using System.Windows.Forms;
using QuickLaunchMenuWinForms.Models;

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
            var logo = new LogoPanel { Dock = DockStyle.Right, Margin = new Padding(0, 4, 0, 4) };
            var logoHost = new Panel { Dock = DockStyle.Right, Width = 240, Padding = new Padding(0, 4, 0, 4) };
            logo.Dock = DockStyle.Fill;
            logoHost.Controls.Add(logo);
            topBar.Controls.Add(appTitle);
            topBar.Controls.Add(logoHost);

            var tabs = new FlatTabControl { Dock = DockStyle.Fill };

            const string lockExplanation = " 🔒 «Общий (HKLM)» — не значит «системный»: так помечен любой пункт, " +
                "записанный для всех пользователей компьютера, а не только текущего. Так делают инсталляторы " +
                "многих обычных программ — изменить/удалить такой пункт отсюда нельзя без прав администратора.";

            var desktopPage = new TabPage("Рабочий стол");
            desktopPage.Controls.Add(new MenuManagerPanel(
                MenuScope.Desktop,
                "Меню правого клика на рабочем столе",
                "Показаны все пункты, включая чужие программы." + lockExplanation));

            var folderPage = new TabPage("Папки");
            folderPage.Controls.Add(new MenuManagerPanel(
                MenuScope.Folder,
                "Меню правого клика НА папке",
                "Программа получает путь к кликнутой папке автоматически." + lockExplanation));

            var filePage = new TabPage("Файлы");
            filePage.Controls.Add(new MenuManagerPanel(
                MenuScope.File,
                "Меню правого клика НА любом файле",
                "Программа получает путь к кликнутому файлу автоматически." + lockExplanation));

            var sendToPage = new TabPage("Отправить");
            sendToPage.Controls.Add(new SendToPanel());

            var extensionsPage = new TabPage("Расширения");
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
    }
}
