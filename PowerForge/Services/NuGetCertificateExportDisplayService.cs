using System;
using System.Collections.Generic;

namespace PowerForge;

internal sealed class NuGetCertificateExportDisplayService
{
    public IReadOnlyList<NuGetCertificateExportDisplayLine> CreateSuccessSummary(NuGetCertificateExportResult result)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));
        if (!result.Success)
            throw new ArgumentException("A successful export result is required.", nameof(result));
        if (string.IsNullOrWhiteSpace(result.CertificatePath))
            throw new ArgumentException("Certificate path is required for display output.", nameof(result));

        return new List<NuGetCertificateExportDisplayLine>
        {
            Line($"Certificate exported successfully to: {result.CertificatePath}", ConsoleColor.Green),
            Line(string.Empty),
            Line("Next steps to register with NuGet.org:", ConsoleColor.Yellow),
            Line("1. Go to https://www.nuget.org and sign in"),
            Line("2. Go to Account Settings > Certificates"),
            Line("3. Click 'Register new'"),
            Line($"4. Upload the file: {result.CertificatePath}"),
            Line("5. Once registered, all future packages must be signed with this certificate"),
            Line(string.Empty),
            Line("Certificate details:", ConsoleColor.Cyan),
            Line($"  Subject: {result.Subject}"),
            Line($"  Issuer: {result.Issuer}"),
            Line($"  Thumbprint: {result.Certificate?.Thumbprint}"),
            Line($"  SHA256: {result.Sha256}"),
            Line($"  Valid From: {result.NotBefore}"),
            Line($"  Valid To: {result.NotAfter}")
        };
    }

    private static NuGetCertificateExportDisplayLine Line(string text, ConsoleColor? color = null)
        => new() { Text = text, Color = color };
}
