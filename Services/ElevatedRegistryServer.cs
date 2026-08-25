using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace QuickLaunchMenuWinForms.Services
{
    /// <summary>Runs inside the elevated helper process started once via UAC (see ElevatedRegistryClient).
    /// Stays alive for as long as the main process keeps the pipe open, applying one HKLM registry write per
    /// request line — so the user only has to click through a single UAC prompt no matter how many
    /// admin-level changes they make in one session. No UI, headless.</summary>
    public static class ElevatedRegistryServer
    {
        public static void Run(string pipeName)
        {
            try
            {
                // This process runs elevated (High integrity), so Windows stamps a pipe we create with a
                // High-integrity mandatory label by default — silently denying WRITE access to our caller,
                // which is the same user account but only Medium integrity (not elevated), even though the
                // DACL below allows it. That's the actual reason the client's Connect() was failing.
                var pipeSecurity = new PipeSecurity();
                pipeSecurity.SetSecurityDescriptorSddlForm("D:(A;;GA;;;AU)");

                using (var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.None, 0, 0, pipeSecurity))
                {
                    LowerPipeIntegrityLabel(server);
                    server.WaitForConnection();

                    using (var reader = new StreamReader(server))
                    using (var writer = new StreamWriter(server) { AutoFlush = true })
                    {
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            writer.WriteLine(HandleRequest(line));
                        }
                    }
                }
            }
            catch
            {
                // The main process disconnected, was never there, or the pipe broke — nothing to recover,
                // just exit quietly so this helper process doesn't linger.
            }
        }

        // .NET's managed PipeSecurity/ObjectSecurity SDDL parser silently drops mandatory-label ("ML") ACEs
        // instead of applying them (verified directly — round-tripping "S:(ML;;NW;;;LW)" through
        // SetSecurityDescriptorSddlForm/GetSecurityDescriptorSddlForm comes back with an empty SACL). The
        // native SDDL parser in advapi32.dll does support it, so build the descriptor there instead and
        // attach it to the pipe with SetKernelObjectSecurity, touching only the label — this is what
        // actually lowers the pipe to Low integrity so our non-elevated caller can read/write it.
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
            string stringSecurityDescriptor, uint stringSDRevision, out IntPtr securityDescriptor, out uint securityDescriptorSize);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetKernelObjectSecurity(IntPtr handle, uint securityInformation, IntPtr securityDescriptor);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private const uint LabelSecurityInformation = 0x00000010;

        private static void LowerPipeIntegrityLabel(PipeStream pipe)
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor("S:(ML;;NW;;;LW)", 1, out var sd, out _)) return;
            try { SetKernelObjectSecurity(pipe.SafePipeHandle.DangerousGetHandle(), LabelSecurityInformation, sd); }
            finally { LocalFree(sd); }
        }

        private static string HandleRequest(string requestJson)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var request = serializer.Deserialize<ElevatedRegistryRequest>(requestJson);

                switch (request.Op)
                {
                    case "delete":
                        Registry.LocalMachine.DeleteSubKeyTree(request.SubPath, throwOnMissingSubKey: false);
                        break;

                    case "setValues":
                        foreach (var write in request.Writes ?? new List<ElevatedRegistryWrite>())
                        {
                            using (var key = Registry.LocalMachine.CreateSubKey(write.Path, writable: true))
                            {
                                if (key == null) throw new InvalidOperationException("Не удалось открыть ключ реестра.");
                                key.SetValue(write.Name, write.Value);
                            }
                        }
                        break;

                    default:
                        using (var key = Registry.LocalMachine.CreateSubKey(request.SubPath, writable: true))
                        {
                            if (key == null) throw new InvalidOperationException("Не удалось открыть ключ реестра.");
                            key.SetValue(null, request.Value);
                        }
                        break;
                }

                return "OK";
            }
            catch (Exception ex)
            {
                return "ERR:" + ex.Message.Replace('\r', ' ').Replace('\n', ' ');
            }
        }
    }
}
