namespace PowerForgeStudio.Domain.Workspace;

public sealed record ReceiptBatchSnapshot<TReceipt>(
    string Headline,
    string Details,
    IReadOnlyList<TReceipt> Items);
