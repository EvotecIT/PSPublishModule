using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PowerForgeStudio.Wpf.ViewModels.Hub;

internal static class FileIconCache
{
    private static readonly ConcurrentDictionary<string, ImageSource> IconsByExtension = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? _folderIcon;

    internal static ImageSource? GetIcon(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            return _folderIcon ??= ExtractIcon(path, isDirectory: true);
        }

        var ext = System.IO.Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
        {
            ext = ".file";
        }

        return IconsByExtension.GetOrAdd(ext, _ => ExtractIcon(path, isDirectory: false)!);
    }

    private static ImageSource? ExtractIcon(string path, bool isDirectory)
    {
        try
        {
            var info = new SHFILEINFO();
            var flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
            var fileAttributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

            var result = SHGetFileInfo(path, fileAttributes, ref info, (uint)Marshal.SizeOf(info), flags);
            if (result == nint.Zero || info.hIcon == nint.Zero)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);
}
