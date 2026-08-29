using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool AreControlledGeneratedOutputsEquivalent(
        string candidatePath,
        string controlledPath)
    {
        byte[] candidateDigest = ComputeGeneratedOutputSha256(candidatePath);
        byte[] controlledDigest = ComputeGeneratedOutputSha256(controlledPath);
        return candidateDigest.SequenceEqual(controlledDigest) &&
               ReadControlledUnixFileMode(candidatePath) ==
               ReadControlledUnixFileMode(controlledPath);
    }

    private static int? ReadControlledUnixFileMode(string path)
    {
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            return (int)File.GetUnixFileMode(path);
#endif
        return null;
    }

    private static byte[] ComputeGeneratedOutputSha256(string path)
    {
        using SHA256 hash = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return hash.ComputeHash(stream);
    }

}
