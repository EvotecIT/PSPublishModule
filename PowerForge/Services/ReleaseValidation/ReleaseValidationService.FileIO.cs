namespace PowerForge;

public sealed partial class ReleaseValidationService
{
    private static async Task CopyValidationFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        cancellationToken.ThrowIfCancellationRequested();
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await input.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
        {
            cancellationToken.ThrowIfCancellationRequested();
            const UnixFileMode ordinaryPermissions = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(destination, File.GetUnixFileMode(source) & ordinaryPermissions);
        }
#endif
    }
}
