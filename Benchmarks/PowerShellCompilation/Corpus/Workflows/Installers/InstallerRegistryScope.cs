using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PowerForge.Corpus
{
    // This fixture is loaded only inside the disposable observer process. The
    // original installers use their unchanged registry and environment APIs.
    public sealed class InstallerRegistryScope : IDisposable
    {
        private static readonly IntPtr CurrentUser = new IntPtr(unchecked((int)0x80000001));
        private static readonly IntPtr LocalMachine = new IntPtr(unchecked((int)0x80000002));
        private readonly string ownedPath = "Software\\PowerForge\\CompilerQualification\\" + Guid.NewGuid().ToString("N");
        private readonly string marker = Guid.NewGuid().ToString("N");
        private readonly string originalUserPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
        private readonly string originalMachinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
        private RegistryKey owned;
        private RegistryKey user;
        private RegistryKey machine;
        private bool userMapped;
        private bool machineMapped;
        private bool disposed;

        [DllImport("advapi32.dll")]
        private static extern int RegOverridePredefKey(IntPtr predefined, IntPtr replacement);

        public InstallerRegistryScope(string powerShellHome)
        {
            try
            {
                owned = Registry.CurrentUser.CreateSubKey(ownedPath);
                user = owned.CreateSubKey("User");
                machine = owned.CreateSubKey("Machine");
                user.SetValue("QualificationOwner", marker);
                machine.SetValue("QualificationOwner", marker);
                using (var environment = user.CreateSubKey("Environment"))
                    environment.SetValue("PATH", "qualification-user", RegistryValueKind.ExpandString);
                using (var environment = machine.CreateSubKey("SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment"))
                    environment.SetValue("PATH", "qualification-machine", RegistryValueKind.ExpandString);
                // Desktop command discovery reads its installation directory
                // after startup, including already-imported qualified commands.
                foreach (var version in new[] { "1", "3" })
                    using (var engine = machine.CreateSubKey("SOFTWARE\\Microsoft\\PowerShell\\" + version + "\\PowerShellEngine"))
                        engine.SetValue("ApplicationBase", powerShellHome);
                // Desktop hashing and Add-Type use registered Windows crypto
                // providers. Preserve their public DLL/algorithm registration.
                const string crypto = "SOFTWARE\\Microsoft\\Cryptography\\Defaults";
                using (var source = Registry.LocalMachine.OpenSubKey(crypto))
                using (var destination = machine.CreateSubKey(crypto))
                    if (source != null) CopyRegistration(source, destination, 0);
                Check(RegOverridePredefKey(CurrentUser, user.Handle.DangerousGetHandle()));
                userMapped = true;
                Check(RegOverridePredefKey(LocalMachine, machine.Handle.DangerousGetHandle()));
                machineMapped = true;
                Verify();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Verify()
        {
            if (!userMapped || !machineMapped ||
                !marker.Equals(Registry.CurrentUser.GetValue("QualificationOwner")) ||
                !marker.Equals(Registry.LocalMachine.GetValue("QualificationOwner")) ||
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) != "qualification-user" ||
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) != "qualification-machine")
                throw new InvalidOperationException("Installer registry isolation was not established.");
        }

        public string ReadPath(bool global)
        {
            using (var key = global
                ? machine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment")
                : user.OpenSubKey("Environment"))
                return (string)key.GetValue("PATH", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }

        public void Dispose()
        {
            if (disposed) return;
            // Restore both roots before deleting the exact owned key. A failed
            // restoration leaves that key available for diagnosis and fails the observer.
            if (machineMapped)
            {
                Check(RegOverridePredefKey(LocalMachine, IntPtr.Zero));
                machineMapped = false;
            }
            if (userMapped)
            {
                Check(RegOverridePredefKey(CurrentUser, IntPtr.Zero));
                userMapped = false;
            }
            if (machine != null) machine.Dispose();
            if (user != null) user.Dispose();
            if (owned != null)
            {
                owned.Dispose();
                Registry.CurrentUser.DeleteSubKeyTree(ownedPath, false);
            }
            disposed = true;
            if (Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) != originalUserPath ||
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) != originalMachinePath)
                throw new InvalidOperationException("The observer did not preserve the real environment PATH values.");
        }

        private static void Check(int status)
        {
            if (status != 0) throw new Win32Exception(status);
        }

        private static void CopyRegistration(RegistryKey source, RegistryKey destination, int depth)
        {
            if (depth > 8) throw new InvalidOperationException("Unexpected provider registration depth.");
            foreach (var name in source.GetValueNames())
                destination.SetValue(name, source.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), source.GetValueKind(name));
            foreach (var name in source.GetSubKeyNames())
                using (var child = source.OpenSubKey(name))
                using (var target = destination.CreateSubKey(name))
                    CopyRegistration(child, target, depth + 1);
        }
    }
}
