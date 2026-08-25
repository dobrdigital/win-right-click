using System;
using System.Windows.Forms;
using QuickLaunchMenuWinForms.Models;
using QuickLaunchMenuWinForms.Services;

namespace QuickLaunchMenuWinForms
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length >= 2 && string.Equals(args[0], "--elevated-server", StringComparison.OrdinalIgnoreCase))
            {
                // Elevated helper invocation (see ElevatedRegistryClient): stays alive, writing HKLM values
                // on request over a named pipe, for as long as the main (non-elevated) process keeps the
                // pipe open — one UAC prompt then covers every admin-level change for the rest of the session.
                ElevatedRegistryServer.Run(args[1]);
                return;
            }

            // Must happen before any window (including native scrollbars/controls) is created —
            // calling it later leaves already-created chrome stuck in light mode.
            Theme.InitializeAppMode();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                if (args.Length >= 2 && string.Equals(args[0], "--add", StringComparison.OrdinalIgnoreCase))
                {
                    var service = new ContextMenuService(MenuScope.Desktop);
                    Application.Run(new AddEditForm(service, MenuScope.Desktop, existingEntry: null, prefillTargetPath: args[1], standalone: true));
                }
                else
                {
                    Application.Run(new MainForm());
                }
            }
            finally
            {
                ElevatedRegistryClient.Shutdown();
            }
        }
    }
}
