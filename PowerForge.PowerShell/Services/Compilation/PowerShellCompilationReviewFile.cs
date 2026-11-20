using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>Reads and writes the dependency review exchanged between compiler hosts.</summary>
public static class PowerShellCompilationReviewFile
{
    /// <summary>Reads a review file for a later build against the selected source and target.</summary>
    public static PowerShellCompilationReview Read(string path)
    {
        return JsonSerializer.Deserialize(File.ReadAllText(path), PowerShellCompilationReviewJsonContext.Default.PowerShellCompilationReview)
               ?? throw new InvalidDataException("The dependency review was empty.");
    }

    /// <summary>Creates a review file without overwriting an existing file or changing module inputs.</summary>
    public static void Write(string path, PowerShellCompilationReview review, string? moduleSourceDirectory)
    {
        if (review is null) throw new ArgumentNullException(nameof(review));
        var fullPath = Path.GetFullPath(path);
        if (moduleSourceDirectory is not null)
        {
            var sourceDirectory = Path.GetFullPath(moduleSourceDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (PowerShellCompilationPathSafety.PathEquals(fullPath, sourceDirectory) ||
                PowerShellCompilationPathSafety.PathStartsWith(fullPath, sourceDirectory + Path.DirectorySeparatorChar))
                throw new ArgumentException("Save the review outside the module source directory so it does not change the reviewed dependency inventory.", nameof(path));
        }

        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var temporary = Path.Combine(directory, $".powerforge-review-{Guid.NewGuid():N}.tmp");
        var staged = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                staged = true;
                JsonSerializer.Serialize(stream, review, PowerShellCompilationReviewJsonContext.Default.PowerShellCompilationReview);
            }
            File.Move(temporary, fullPath);
            staged = false;
        }
        finally
        {
            if (staged) File.Delete(temporary);
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PowerShellCompilationReview))]
internal partial class PowerShellCompilationReviewJsonContext : JsonSerializerContext
{
}
