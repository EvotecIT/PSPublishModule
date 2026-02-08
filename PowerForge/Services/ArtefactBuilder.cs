using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Creates packed/unpacked artefacts for a built module using typed configuration segments.
/// </summary>
public sealed partial class ArtefactBuilder
{
    private static readonly string[] DefaultExcludeFromPackage = { ".*", "Ignore", "Examples", "package.json", "Publish", "Docs" };
    private static readonly string[] DefaultIncludeRoot = { "*.psm1", "*.psd1", "*.Libraries.ps1", "License*" };
    private static readonly string[] DefaultIncludePS1 = { "Private", "Public", "Enums", "Classes" };
    private static readonly string[] DefaultIncludeAll = { "Images", "Resources", "Templates", "Bin", "Lib", "Data", "en-US" };

    private const string PSGalleryName = "PSGallery";
    private const string PSGalleryUriV2 = "https://www.powershellgallery.com/api/v2";

    private readonly ILogger _logger;
    private bool _skipPsResourceGetSave;
    private bool _ensuredPowerShellGetRepository;
    private bool _ensuredPsResourceGetRepository;

    /// <summary>
    /// Creates a new builder that logs progress via <paramref name="logger"/>.
    /// </summary>
    public ArtefactBuilder(ILogger logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

}
