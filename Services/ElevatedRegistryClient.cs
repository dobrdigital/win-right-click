using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace QuickLaunchMenuWinForms.Services
{
    /// <summary>Lazily starts one elevated helper process (a single UAC prompt) the first time an HKLM write
    /// is needed, then reuses it via a named pipe for every subsequent write in this session — so toggling
    /// several shell extensions in a row only asks for admin approval once, not once per row.</summary>
    public static class ElevatedRegistryClient
    {
        private static readonly object Lock = new object();
        private static Process? _serverProcess;
        private static NamedPipeClientStream? _pipe;
        private static StreamReader? _reader;
        private static StreamWriter? _writer;

        /// <summary>Writes the default (unnamed) value of an HKLM key. Returns true on success, false if
        /// elevation was declined or the write otherwise failed — caller should fall back to messaging
        /// the user as it would if there were no helper at all.</summary>
        public static bool TrySetValue(string subPath, string value) =>
            SendRequest(new ElevatedRegistryRequest { Op = "set", SubPath = subPath, Value = value });

        /// <summary>Deletes an HKLM key and everything under it. Returns true on success, false if elevation
        /// was declined or the delete otherwise failed.</summary>
        public static bool TryDeleteTree(string subPath) =>
            SendRequest(new ElevatedRegistryRequest { Op = "delete", SubPath = subPath });

        /// <summary>Writes one or more named values under HKLM, possibly across different keys (e.g. an
        /// entry's own values plus its "command" subkey's default value) — used to edit an existing
        /// protected link's target/icon/display name in place without renaming or moving its key.</summary>
        public static bool TrySetValues(List<ElevatedRegistryWrite> writes) =>
            SendRequest(new ElevatedRegistryRequest { Op = "setValues", Writes = writes });

        private static bool SendRequest(ElevatedRegistryRequest request)
        {
            lock (Lock)
            {
                if (!EnsureConnected()) return false;

                try
                {
                    var serializer = new JavaScriptSerializer();
                    _writer!.WriteLine(serializer.Serialize(request));
                    return _reader!.ReadLine() == "OK";
                }
                catch
                {
                    // The helper process died mid-session — drop it so the next call starts a fresh one
                    // (with a fresh UAC prompt) instead of silently failing forever.
                    Disconnect();
                    return false;
                }
            }
        }

        private static bool EnsureConnected()
        {
            if (_pipe != null && _pipe.IsConnected) return true;
            Disconnect();

            var pipeName = "QuickLaunchMenu-Elevated-" + Guid.NewGuid().ToString("N");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = $"--elevated-server {pipeName}",
                    Verb = "runas",
                    UseShellExecute = true
                };
                _serverProcess = Process.Start(psi);
            }
            catch (Win32Exception)
            {
                return false; // user declined the UAC prompt
            }

            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            try
            {
                pipe.Connect(10000);
            }
            catch
            {
                pipe.Dispose();
                return false;
            }

            _pipe = pipe;
            _reader = new StreamReader(_pipe);
            _writer = new StreamWriter(_pipe) { AutoFlush = true };
            return true;
        }

        private static void Disconnect()
        {
            try { _writer?.Dispose(); } catch { /* best-effort cleanup */ }
            try { _reader?.Dispose(); } catch { /* best-effort cleanup */ }
            try { _pipe?.Dispose(); } catch { /* best-effort cleanup */ }
            _writer = null;
            _reader = null;
            _pipe = null;
            _serverProcess = null;
        }

        /// <summary>Closes the pipe so the elevated helper (blocked on ReadLine) sees end-of-stream and exits
        /// on its own — call this once, when the app is shutting down, so it never lingers as an orphan.</summary>
        public static void Shutdown()
        {
            lock (Lock) Disconnect();
        }
    }
}
