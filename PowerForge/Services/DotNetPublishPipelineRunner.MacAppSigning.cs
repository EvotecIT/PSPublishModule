namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static void PrepareMacSignedPayload(string codeRoot, string resourcesRoot)
    {
        foreach (string file in Directory.EnumerateFiles(codeRoot, "*", SearchOption.AllDirectories).ToArray())
        {
            if (IsMacNativeCode(file)) continue;
            if (Path.GetExtension(file).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Certificate-signed Mac packaging requires a single-file publish with native libraries left outside the bundle; managed DLLs cannot be placed in Contents/MacOS.");
            string destination = Path.Combine(resourcesRoot, FrameworkCompatibility.GetRelativePath(codeRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(file, destination);
        }
        foreach (string directory in Directory.EnumerateDirectories(codeRoot, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }

    internal static bool IsMacNativeCode(string path)
    {
        using var stream = File.OpenRead(path);
        var magic = new byte[4];
        if (stream.Read(magic, 0, magic.Length) != 4) return false;
        uint value = ((uint)magic[0] << 24) | ((uint)magic[1] << 16) | ((uint)magic[2] << 8) | magic[3];
        return value is 0xfeedface or 0xcefaedfe or 0xfeedfacf or 0xcffaedfe or 0xcafebabe or 0xbebafeca or 0xcafebabf or 0xbfbafeca;
    }

    private static void SignMacNestedCode(DotNetPublishMacAppOptions options, string codeRoot, string workingDirectory)
    {
        foreach (string path in Directory.EnumerateFiles(codeRoot, "*", SearchOption.AllDirectories)
                     .Where(IsMacNativeCode).Where(path => Path.GetFileName(path) != options.Executable)
                     .OrderByDescending(path => path.Length))
        {
            var arguments = new List<string> { "--force", "--sign", options.CodesignIdentity };
            if (ShouldEnableMacHardenedRuntime(options)) arguments.AddRange(new[] { "--options", "runtime" });
            arguments.Add(options.Timestamp ? "--timestamp" : "--timestamp=none");
            arguments.Add(path);
            RunRequiredMacTool("/usr/bin/codesign", workingDirectory, arguments);
        }
    }

}
