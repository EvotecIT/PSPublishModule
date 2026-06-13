using System.Runtime.InteropServices;

namespace PowerForge;

internal sealed class MsiPackageMetadataReader
{
    private static readonly string[] DefaultPropertyNames =
    {
        "ProductCode",
        "ProductName",
        "ProductVersion",
        "Manufacturer",
        "UpgradeCode"
    };

    public DotNetPublishMsiPackageMetadata Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("MSI path must not be empty.", nameof(path));

        var fullPath = System.IO.Path.GetFullPath(path);
        var values = ReadProperties(fullPath, DefaultPropertyNames);

        return new DotNetPublishMsiPackageMetadata
        {
            Path = fullPath,
            ProductCode = GetValue(values, "ProductCode"),
            ProductName = GetValue(values, "ProductName"),
            ProductVersion = GetValue(values, "ProductVersion"),
            Manufacturer = GetValue(values, "Manufacturer"),
            UpgradeCode = GetValue(values, "UpgradeCode")
        };
    }

    internal static IReadOnlyDictionary<string, string> ReadProperties(string path, IEnumerable<string> propertyNames)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("MSI path must not be empty.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("MSI file not found.", path);

        var names = (propertyNames ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

#if !NET472
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Installer automation is only available on Windows.");
#endif
        var installerType = Type.GetTypeFromProgID("WindowsInstaller.Installer");
        if (installerType is null)
            throw new PlatformNotSupportedException("Windows Installer automation is not available on this platform.");

        object? installer = null;
        object? database = null;
        object? view = null;
        try
        {
            installer = Activator.CreateInstance(installerType)
                ?? throw new InvalidOperationException("Could not create WindowsInstaller.Installer.");
            database = InvokeComMethod(installer, "OpenDatabase", path, 0);
            if (database is null)
                throw new InvalidOperationException($"Could not open MSI database: {path}");

            var conditions = names
                .Select(name => $"`Property` = '{name.Replace("'", "''")}'");
            var query = "SELECT `Property`, `Value` FROM `Property` WHERE " + string.Join(" OR ", conditions);
            view = InvokeComMethod(database, "OpenView", query);
            if (view is null)
                throw new InvalidOperationException($"Could not open MSI property view: {path}");
            InvokeComMethod(view, "Execute");

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (InvokeComMethod(view, "Fetch") is { } record)
            {
                try
                {
                    var key = ReadRecordString(record, 1);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        key = key!.Trim();
                        values[key] = ReadRecordString(record, 2) ?? string.Empty;
                    }
                }
                finally
                {
                    ReleaseComObject(record);
                }
            }

            return values;
        }
        finally
        {
            ReleaseComObject(view);
            ReleaseComObject(database);
            ReleaseComObject(installer);
        }
    }

    private static string? GetValue(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static object? InvokeComMethod(object target, string name, params object?[] args)
    {
        return target.GetType().InvokeMember(
            name,
            System.Reflection.BindingFlags.InvokeMethod,
            binder: null,
            target,
            args.Length == 0 ? null : args);
    }

    private static string? ReadRecordString(object record, int index)
    {
        return record.GetType().InvokeMember(
            "StringData",
            System.Reflection.BindingFlags.GetProperty,
            binder: null,
            target: record,
            args: new object[] { index }) as string;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is null)
            return;

#if NET472
        if (Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
#else
        if (OperatingSystem.IsWindows() && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
#endif
    }
}
