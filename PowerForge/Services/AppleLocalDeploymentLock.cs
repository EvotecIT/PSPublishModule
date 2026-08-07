using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

internal static class AppleLocalDeploymentLock
{
    internal static FileStream Acquire(string key, string description)
    {
        var lockRoot = Path.Combine(Path.GetTempPath(), "powerforge-apple-local", "locks");
        Directory.CreateDirectory(lockRoot);
        var lockPath = Path.Combine(lockRoot, $"{HashKey(key)}.lock");
        try
        {
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Another Apple deployment is already using {description}.", exception);
        }
    }

    private static string HashKey(string key)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
        return BitConverter.ToString(hash, 0, 12).Replace("-", string.Empty).ToLowerInvariant();
    }
}
