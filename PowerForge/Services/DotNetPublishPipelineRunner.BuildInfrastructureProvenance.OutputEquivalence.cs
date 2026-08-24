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
        return candidateDigest.SequenceEqual(controlledDigest);
    }

    private static byte[] ComputeGeneratedOutputSha256(string path)
    {
        using SHA256 hash = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return hash.ComputeHash(stream);
    }

}
