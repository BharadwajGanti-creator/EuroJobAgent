using System.Text.Json;

/// <summary>
/// tracker.json is the whole persistence layer: a dictionary of JobId -> TrackedJob, committed
/// back to the repo by the daily workflow (same pattern as the existing agentic-job-search
/// project's wwwroot/latest_matches.json commit-back). No database, no external service.
///
/// You update the "Status" field yourself (New / Applied / Interview / Rejected / Offer) either
/// by hand-editing the file or via `dotnet run -- mark <jobId> <Status>`, then commit.
/// </summary>
public static class TrackerStore
{
    private const string TrackerPath = "tracker.json";
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Dictionary<string, TrackedJob> Load()
    {
        if (!File.Exists(TrackerPath)) return new();
        var json = File.ReadAllText(TrackerPath);
        if (string.IsNullOrWhiteSpace(json)) return new();
        return JsonSerializer.Deserialize<Dictionary<string, TrackedJob>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }

    public static void Save(Dictionary<string, TrackedJob> tracker) =>
        File.WriteAllText(TrackerPath, JsonSerializer.Serialize(tracker, Opts));

    /// <summary>CLI helper: `dotnet run -- mark <jobId-prefix> <Status>`</summary>
    public static int Mark(string jobIdPrefix, string status)
    {
        var tracker = Load();
        var matches = tracker.Keys.Where(k => k.StartsWith(jobIdPrefix, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 0)
        {
            Console.WriteLine($"No job found with ID starting '{jobIdPrefix}'.");
            return 1;
        }
        if (matches.Count > 1)
        {
            Console.WriteLine($"Ambiguous prefix '{jobIdPrefix}' matches {matches.Count} jobs. Use more characters.");
            return 1;
        }

        tracker[matches[0]].Status = status;
        Save(tracker);
        Console.WriteLine($"Marked {matches[0]} ({tracker[matches[0]].Title} @ {tracker[matches[0]].Company}) as '{status}'. Now commit tracker.json.");
        return 0;
    }
}
