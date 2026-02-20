namespace PrStats.Services;

public sealed class BotFilter
{
    private readonly HashSet<string> _botNames;
    private readonly HashSet<string> _botIds;

    public BotFilter(IEnumerable<string> configuredBotNames, IEnumerable<string>? configuredBotIds = null)
    {
        _botNames = new HashSet<string>(configuredBotNames, StringComparer.OrdinalIgnoreCase);
        _botIds = new HashSet<string>(configuredBotIds ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public bool IsBot(string displayName, bool isContainer, string? userId = null)
    {
        if (isContainer)
            return true;

        if (_botNames.Contains(displayName))
            return true;

        if (!string.IsNullOrEmpty(userId) && _botIds.Contains(userId))
            return true;

        return false;
    }
}
