namespace PowerForge;

internal static class ManagedModuleFileComparer
{
    internal static bool HaveSameContents(string leftPath, string rightPath)
    {
        if (!File.Exists(rightPath))
            return false;

        var leftLength = new FileInfo(leftPath).Length;
        if (leftLength != new FileInfo(rightPath).Length)
            return false;

        const int bufferSize = 81920;
        var leftBuffer = new byte[bufferSize];
        var rightBuffer = new byte[bufferSize];
        using var left = new FileStream(
            leftPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize,
            FileOptions.SequentialScan);
        using var right = new FileStream(
            rightPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize,
            FileOptions.SequentialScan);
        while (true)
        {
            var leftRead = left.Read(leftBuffer, 0, leftBuffer.Length);
            var rightRead = right.Read(rightBuffer, 0, rightBuffer.Length);
            if (leftRead != rightRead)
                return false;
            if (leftRead == 0)
                return true;

            for (var index = 0; index < leftRead; index++)
            {
                if (leftBuffer[index] != rightBuffer[index])
                    return false;
            }
        }
    }
}
