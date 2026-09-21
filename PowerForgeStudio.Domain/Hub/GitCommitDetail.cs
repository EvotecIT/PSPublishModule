namespace PowerForgeStudio.Domain.Hub;

public sealed record GitCommitDetail(
    string Hash,
    IReadOnlyList<string> ChangedFiles,
    string Diff,
    bool ChangedFilesTruncated,
    bool DiffTruncated);
