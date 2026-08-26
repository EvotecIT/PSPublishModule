namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool IsControlledTaskDirectoryInput(string taskName, string attributeName)
        => ControlledTaskDirectoryInputAttributes.TryGetValue(taskName, out string[]? attributes) &&
           attributes.Contains(attributeName, StringComparer.OrdinalIgnoreCase);

    private static bool HasOnlyControlledDirectoryTaskInput(
        string directoryPath,
        string allowedRoot,
        Func<string, bool>? isControlledInput)
    {
        try
        {
            string fullDirectoryPath = Path.GetFullPath(directoryPath);
            if (!IsSameOrBelowBuildInputPath(fullDirectoryPath, allowedRoot))
            {
                return false;
            }

            if (!Directory.Exists(fullDirectoryPath))
            {
                string virtualDirectoryProbe = fullDirectoryPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return isControlledInput is not null && isControlledInput(virtualDirectoryProbe);
            }

            if (HasReparsePointBelowRoot(fullDirectoryPath, allowedRoot) ||
                (isControlledInput is not null && !isControlledInput(fullDirectoryPath)))
            {
                return false;
            }

            var pending = new Stack<string>();
            pending.Push(fullDirectoryPath);
            int inspectedEntries = 0;
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (++inspectedEntries > MaximumControlledTaskFileInputExpressions)
                        return false;
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                        (isControlledInput is not null && !isControlledInput(entry)))
                    {
                        return false;
                    }
                    if ((attributes & FileAttributes.Directory) != 0)
                        pending.Push(entry);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
