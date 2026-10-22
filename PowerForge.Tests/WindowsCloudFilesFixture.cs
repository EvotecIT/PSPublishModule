using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PowerForge.Tests;

/// <summary>Creates hydrated Cloud Files inputs in an isolated, disconnected sync root.</summary>
internal sealed class WindowsCloudFilesFixture : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
    internal string CloudRoot { get; }
    private bool _registered;
    private readonly sbyte _originalPlaceholderMode;

    internal WindowsCloudFilesFixture()
    {
        CloudRoot = Directory.CreateDirectory(Path.Combine(Root, "cloud")).FullName;
        var registration = new SyncRegistration
        {
            StructSize = (uint)Marshal.SizeOf<SyncRegistration>(),
            ProviderName = "PowerForge isolated test",
            ProviderVersion = "1.0",
            ProviderId = new Guid("6778fe13-2776-4518-9e79-0e9f536c5a41")
        };
        var policies = new SyncPolicies
        {
            StructSize = (uint)Marshal.SizeOf<SyncPolicies>(),
            // FULL policies retain cloud metadata. All test content stays hydrated
            // and fully populated; no provider, downloads or callbacks are needed.
            HydrationPrimary = 2,
            PopulationPrimary = 2
        };
        // Windows can disguise cloud metadata for a host such as VSTest. Expose it
        // on this synchronous test thread so the regression really reaches the guard.
        _originalPlaceholderMode = RtlSetThreadPlaceholderCompatibilityMode(2);
        if (_originalPlaceholderMode < 0)
        {
            Directory.Delete(Root, recursive: true);
            throw new InvalidOperationException("Cannot expose Cloud Files metadata on the test thread.");
        }
        try
        {
            // This disconnected test provider already owns the complete local namespace.
            // Do not ask it to populate the root through a callback on first access.
            const uint disableOnDemandPopulationOnRoot = 0x00000002;
            Marshal.ThrowExceptionForHR(CfRegisterSyncRoot(CloudRoot, in registration, in policies,
                disableOnDemandPopulationOnRoot));
            _registered = true;
        }
        catch
        {
            try { Directory.Delete(Root, recursive: true); }
            finally { RtlSetThreadPlaceholderCompatibilityMode(_originalPlaceholderMode); }
            throw;
        }
    }

    internal void Convert(string path)
    {
        byte[] identity = Encoding.UTF8.GetBytes(Path.GetRelativePath(CloudRoot, path));
        using (var handle = CreateFile(path, 0x40000000, FileShare.ReadWrite | FileShare.Delete,
                   IntPtr.Zero, FileMode.Open, 0x02000000, IntPtr.Zero))
        {
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            int result = CfConvertToPlaceholder(handle, identity, (uint)identity.Length,
                1, IntPtr.Zero, IntPtr.Zero);
            Marshal.ThrowExceptionForHR(result);
            Assert.Equal(0, result);
        }
        Assert.True((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0,
            $"Fixture must exercise real cloud reparse points at '{path}'. Attributes: {File.GetAttributes(path)}; native cloud metadata: {WindowsCloudFiles.IsPlaceholder(path)}.");
    }

    public void Dispose()
    {
        try
        {
            if (_registered)
            {
                Marshal.ThrowExceptionForHR(CfUnregisterSyncRoot(CloudRoot));
                _registered = false;
            }
            Directory.Delete(Root, recursive: true);
        }
        finally { RtlSetThreadPlaceholderCompatibilityMode(_originalPlaceholderMode); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SyncRegistration
    {
        internal uint StructSize;
        [MarshalAs(UnmanagedType.LPWStr)] internal string ProviderName;
        [MarshalAs(UnmanagedType.LPWStr)] internal string ProviderVersion;
        internal IntPtr SyncRootIdentity;
        internal uint SyncRootIdentityLength;
        internal IntPtr FileIdentity;
        internal uint FileIdentityLength;
        internal Guid ProviderId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SyncPolicies
    {
        internal uint StructSize;
        internal ushort HydrationPrimary;
        internal ushort HydrationModifier;
        internal ushort PopulationPrimary;
        internal ushort PopulationModifier;
        internal uint InSync;
        internal uint HardLink;
        internal uint PlaceholderManagement;
    }

    [DllImport("cldapi.dll", CharSet = CharSet.Unicode)]
    private static extern int CfRegisterSyncRoot(string path, in SyncRegistration registration, in SyncPolicies policies, uint flags);

    [DllImport("cldapi.dll", CharSet = CharSet.Unicode)]
    private static extern int CfUnregisterSyncRoot(string path);

    [DllImport("cldapi.dll")]
    private static extern int CfConvertToPlaceholder(SafeFileHandle handle, byte[] identity, uint identityLength,
        uint flags, IntPtr usn, IntPtr overlapped);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern sbyte RtlSetThreadPlaceholderCompatibilityMode(sbyte mode);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, FileShare share,
        IntPtr securityAttributes, FileMode disposition, uint flags, IntPtr template);
}
