using System.Collections.Generic;

namespace QuickLaunchMenuWinForms.Services
{
    /// <summary>One HKLM value to write: Name is null for the key's default (unnamed) value.</summary>
    public class ElevatedRegistryWrite
    {
        public string Path { get; set; } = "";
        public string? Name { get; set; }
        public string Value { get; set; } = "";
    }

    /// <summary>Wire format between ElevatedRegistryClient (unelevated) and ElevatedRegistryServer (elevated
    /// helper), serialized as one JSON line per request over the named pipe.</summary>
    public class ElevatedRegistryRequest
    {
        /// <summary>"set" (SubPath/Value — the key's default value), "delete" (SubPath — whole subtree),
        /// or "setValues" (Writes — one or more named values, possibly across different keys).</summary>
        public string Op { get; set; } = "set";
        public string? SubPath { get; set; }
        public string? Value { get; set; }
        public List<ElevatedRegistryWrite>? Writes { get; set; }
    }
}
