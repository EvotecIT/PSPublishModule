using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge.Web;

internal static class WebSearchIdentityHasher
{
    internal static string Compute(params string?[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> lengthPrefix = stackalloc byte[sizeof(int)];
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, bytes.Length);
            hash.AppendData(lengthPrefix);
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
