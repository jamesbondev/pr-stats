using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrStats.Models;

namespace PrStats.Services;

public static class PrCache
{
    public const int SchemaVersion = 1;
    public const int EvictionDays = 180;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string GetCachePath(string org, string project)
    {
        var key = $"{org.ToLowerInvariant()}|{project.ToLowerInvariant()}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var hash = Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PrStats", "cache", $"{hash}-{project}.json");
    }

    public static async Task<Dictionary<int, PullRequestData>> LoadAsync(string org, string project)
    {
        var path = GetCachePath(org, project);

        if (!File.Exists(path))
            return new Dictionary<int, PullRequestData>();

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var cache = JsonSerializer.Deserialize<CacheRoot>(json, s_jsonOptions);

            if (cache is null)
                return new Dictionary<int, PullRequestData>();

            if (cache.SchemaVersion != SchemaVersion)
            {
                Console.WriteLine($"Warning: Cache schema version mismatch (found {cache.SchemaVersion}, expected {SchemaVersion}). Discarding cache.");
                return new Dictionary<int, PullRequestData>();
            }

            return cache.PullRequests ?? new Dictionary<int, PullRequestData>();
        }
        catch (JsonException)
        {
            Console.WriteLine("Warning: Cache file is corrupt. Discarding cache.");
            return new Dictionary<int, PullRequestData>();
        }
    }

    public static async Task SaveAsync(string org, string project, Dictionary<int, PullRequestData> allPrs)
    {
        var path = GetCachePath(org, project);
        var cutoff = DateTime.UtcNow.AddDays(-EvictionDays);

        // Evict old PRs
        var evicted = new Dictionary<int, PullRequestData>(allPrs.Count);
        foreach (var (id, pr) in allPrs)
        {
            var relevantDate = pr.ClosedDate ?? pr.CreationDate;
            if (pr.CreationDate > relevantDate)
                relevantDate = pr.CreationDate;

            if (relevantDate >= cutoff)
                evicted[id] = pr;
        }

        var cache = new CacheRoot
        {
            SchemaVersion = SchemaVersion,
            Organization = org,
            Project = project,
            PullRequests = evicted,
        };

        var json = JsonSerializer.Serialize(cache, s_jsonOptions);

        try
        {
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);

            var tempPath = path + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Warning: Failed to write cache: {ex.Message}");
        }
    }

    public static void DeleteCache(string org, string project)
    {
        var path = GetCachePath(org, project);
        if (File.Exists(path))
            File.Delete(path);
    }

    private sealed class CacheRoot
    {
        public int SchemaVersion { get; init; }
        public string Organization { get; init; } = "";
        public string Project { get; init; } = "";
        public Dictionary<int, PullRequestData>? PullRequests { get; init; }
    }
}
