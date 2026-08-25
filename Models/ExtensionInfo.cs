namespace QuickLaunchMenuWinForms.Models
{
    /// <summary>A COM-based shell extension (shellex\ContextMenuHandlers) — its actual menu items can't be
    /// listed (that would require running the extension's code), but it can be safely enabled/disabled
    /// using the same reversible "-" CLSID prefix trick tools like ShellExView use.</summary>
    public class ExtensionInfo
    {
        /// <summary>Registry path of the handler key, relative to Software\Classes (e.g. Directory\shellex\ContextMenuHandlers\7-Zip).</summary>
        public string HandlerSubPath { get; set; } = string.Empty;

        public bool IsHklm { get; set; }

        /// <summary>The (Default) value exactly as stored — includes the leading '-' when disabled.</summary>
        public string RawClsid { get; set; } = string.Empty;

        public string Clsid => RawClsid.StartsWith("-") ? RawClsid.Substring(1) : RawClsid;
        public bool IsDisabled => RawClsid.StartsWith("-");

        public string? DllPath { get; set; }
        public string? Company { get; set; }
        public string? FileDescription { get; set; }
    }
}
