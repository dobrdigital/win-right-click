using Microsoft.Win32;

namespace QuickLaunchMenuWinForms.Services
{
    public enum AppLanguage
    {
        Ru,
        En
    }

    /// <summary>Tiny inline translation helper — call T("русский текст", "english text") right where a
    /// string is used, instead of keeping a separate resource catalog. The chosen language is saved under
    /// HKCU so it survives restarts; switching language relaunches the whole process (see MainForm) rather
    /// than trying to retext an already-built UI tree live.</summary>
    public static class Localization
    {
        private const string RegistryPath = @"Software\WIN.right.CLICK";

        public static AppLanguage Current { get; set; } = Load();

        public static string T(string ru, string en) => Current == AppLanguage.Ru ? ru : en;

        private static AppLanguage Load()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    return (key?.GetValue("Language") as string) == "Ru" ? AppLanguage.Ru : AppLanguage.En;
                }
            }
            catch
            {
                return AppLanguage.En;
            }
        }

        public static void Save(AppLanguage lang)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    key?.SetValue("Language", lang.ToString());
                }
            }
            catch
            {
                // Non-fatal — worst case the choice doesn't persist across restarts.
            }
        }
    }
}
