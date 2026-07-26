using System.Text.Json;

/// <summary>
/// tracker.json is the whole persistence layer: a dictionary of JobId -> TrackedJob, committed
/// back to the repo by the daily workflow (same pattern as the existing agentic-job-search
/// project's wwwroot/latest_matches.json commit-back). No database, no external service.
///
/// You update the "Status" field yourself (New / Applied / Interview / Rejected / Offer) either
/// by hand-editing the file, via the web dashboard, or via `dotnet run -- mark <jobId> <Status>`,
/// then commit.
/// </summary>
public static class TrackerStore
{
    public const string DefaultPath = "tracker.json";
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // path is overridable so tests can point at a temp file instead of the real tracker.json.
    public static Dictionary<string, TrackedJob> Load(string path = DefaultPath)
    {
        if (!File.Exists(path)) return new();
        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json)) return new();
        return JsonSerializer.Deserialize<Dictionary<string, TrackedJob>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }

    public static void Save(Dictionary<string, TrackedJob> tracker, string path = DefaultPath) =>
        File.WriteAllText(path, JsonSerializer.Serialize(tracker, Opts));

    /// <summary>CLI helper: `dotnet run -- mark <jobId-prefix> <Status>`</summary>
    public static int Mark(string jobIdPrefix, string status, string path = DefaultPath)
    {
        var tracker = Load(path);
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
        Save(tracker, path);
        Console.WriteLine($"Marked {matches[0]} ({tracker[matches[0]].Title} @ {tracker[matches[0]].Company}) as '{status}'. Now commit tracker.json.");
        return 0;
    }
}
