using System.Runtime.InteropServices;

namespace PowerForge;

internal sealed class ManagedModuleAuthenticodeVerifier
{
    private static readonly string[] SignableExtensions =
    {
        ".ps1",
        ".psm1",
        ".psd1",
        ".ps1xml",
        ".pssc",
        ".psrc",
        ".dll",
        ".exe",
        ".cat"
    };

    public ManagedModuleAuthenticodeVerificationResult VerifyDirectory(string modulePath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException(
                "Managed module Authenticode validation is currently supported only on Windows.");
        }

        if (string.IsNullOrWhiteSpace(modulePath) || !Directory.Exists(modulePath))
            return new ManagedModuleAuthenticodeVerificationResult();

        var root = Path.GetFullPath(modulePath);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(IsSignableFile)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var file in files)
        {
            var status = WinTrust.Verify(file);
            if (status != 0)
            {
                throw new ManagedModuleAuthenticodeException(
                    $"Managed module Authenticode validation failed for '{file}' with status 0x{status:X8}.",
                    file,
                    status);
            }
        }

        var catalogFiles = files
            .Where(IsCatalogFile)
            .Select(file => GetRelativePath(root, file))
            .ToArray();

        return new ManagedModuleAuthenticodeVerificationResult
        {
            CheckedFiles = files.Length,
            Files = files.Select(file => GetRelativePath(root, file)).ToArray(),
            CatalogFiles = catalogFiles.Length,
            CatalogFilePaths = catalogFiles
        };
    }

    private static bool IsSignableFile(string path)
        => SignableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool IsCatalogFile(string path)
        => string.Equals(Path.GetExtension(path), ".cat", StringComparison.OrdinalIgnoreCase);

    private static string GetRelativePath(string root, string file)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return file.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? file.Substring(normalizedRoot.Length)
            : file;
    }

    private static class WinTrust
    {
        private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        public static int Verify(string filePath)
        {
            var fileInfo = new WinTrustFileInfo(filePath);
            var data = new WinTrustData();
            var fileInfoHandle = IntPtr.Zero;
            var dataHandle = IntPtr.Zero;

            try
            {
                fileInfoHandle = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
                Marshal.StructureToPtr(fileInfo, fileInfoHandle, fDeleteOld: false);
                data.File = fileInfoHandle;

                dataHandle = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustData>());
                Marshal.StructureToPtr(data, dataHandle, fDeleteOld: false);

                var result = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, dataHandle);
                data = Marshal.PtrToStructure<WinTrustData>(dataHandle);
                data.StateAction = WinTrustStateAction.Close;
                Marshal.StructureToPtr(data, dataHandle, fDeleteOld: true);
                _ = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, dataHandle);
                return result;
            }
            finally
            {
                if (dataHandle != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(dataHandle);
                if (fileInfoHandle != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(fileInfoHandle);
            }
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
        private static extern int WinVerifyTrust(
            IntPtr hwnd,
            [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
            IntPtr data);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo
    {
        public uint StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
        public string FilePath;
        public IntPtr File = IntPtr.Zero;
        public IntPtr KnownSubject = IntPtr.Zero;

        public WinTrustFileInfo(string filePath) => FilePath = filePath;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public WinTrustUiChoice UiChoice;
        public WinTrustRevocationChecks RevocationChecks;
        public WinTrustUnionChoice UnionChoice;
        public IntPtr File;
        public WinTrustStateAction StateAction;
        public IntPtr StateData;
        public string? UrlReference;
        public WinTrustProviderFlags ProviderFlags;
        public WinTrustUiContext UiContext;

        public WinTrustData()
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustData>();
            PolicyCallbackData = IntPtr.Zero;
            SipClientData = IntPtr.Zero;
            UiChoice = WinTrustUiChoice.None;
            RevocationChecks = WinTrustRevocationChecks.WholeChain;
            UnionChoice = WinTrustUnionChoice.File;
            File = IntPtr.Zero;
            StateAction = WinTrustStateAction.Verify;
            StateData = IntPtr.Zero;
            UrlReference = null;
            ProviderFlags = WinTrustProviderFlags.RevocationCheckChain;
            UiContext = WinTrustUiContext.Execute;
        }
    }

    private enum WinTrustUiChoice : uint
    {
        None = 2
    }

    private enum WinTrustRevocationChecks : uint
    {
        WholeChain = 1
    }

    private enum WinTrustUnionChoice : uint
    {
        File = 1
    }

    private enum WinTrustStateAction : uint
    {
        Verify = 1,
        Close = 2
    }

    [Flags]
    private enum WinTrustProviderFlags : uint
    {
        RevocationCheckChain = 0x00000040
    }

    private enum WinTrustUiContext : uint
    {
        Execute = 0
    }
}
