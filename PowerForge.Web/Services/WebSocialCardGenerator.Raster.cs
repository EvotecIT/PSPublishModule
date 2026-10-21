using System.Diagnostics;

namespace PowerForge.Web;

internal static partial class WebSocialCardGenerator
{
    internal static byte[]? RenderPng(SocialCardRenderOptions options)
    {
        ValidateConfiguredFontFamilies(options);
        try
        {
            return RenderPngCore(options);
        }
        catch (ConfiguredCardFontException ex)
        {
            // Keep the public failure contract independent of the internal classification.
            throw new InvalidOperationException(ex.Message, ex);
        }
        catch (Exception ex)
        {
            // Both external and in-process raster paths can fail while composing artwork.
            // Let the caller's required/optional policy decide whether to stop the build.
            Trace.TraceWarning($"Social card PNG render failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
