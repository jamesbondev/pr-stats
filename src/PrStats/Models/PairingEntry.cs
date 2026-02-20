namespace PrStats.Models;

public sealed class PairingEntry
{
    public required string Author { get; init; }
    public required string Reviewer { get; init; }
    public required int Count { get; init; }
}
