using System.Text.Json;

/// <summary>
/// Ported from the original agentic-job-search project's Eval.cs, adapted to CandidateProfile.
/// Measures the Matcher agent's precision/recall against a hand-labelled golden_set.json, so
/// changes to the matching prompt can be judged by a number instead of a vibe.
///
/// Grow golden_set.json over time: run `dotnet run -- dump` to fetch a fresh batch of real,
/// unlabelled postings into jobs_dump.json, hand-label each one's "relevant" field, then move
/// entries you're confident about into golden_set.json.
/// </summary>
public static class Eval
{
    private const int Threshold = 60; // brain calls a job "relevant" at >= 60

    public static async Task RunAsync()
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var profile = JsonSerializer.Deserialize<CandidateProfile>(
            File.ReadAllText(AppConfig.CandidateProfilePath), opts)!;

        if (!File.Exists("golden_set.json"))
        {
            Console.WriteLine("golden_set.json not found or empty. Run `dotnet run -- dump` first, " +
                               "hand-label some entries, then save them here before running eval.");
            return;
        }
        var golden = JsonSerializer.Deserialize<GoldenItem[]>(File.ReadAllText("golden_set.json"), opts)!;
        if (golden.Length == 0)
        {
            Console.WriteLine("golden_set.json is empty. Nothing to evaluate against yet.");
            return;
        }

        string target = "Mid-level backend .NET/C# developer seeking visa-sponsored relocation " +
                        $"to: {string.Join(" / ", AppConfig.TargetLocations)}. " +
                        "Banking, insurance, and Microsoft-stack consultancy roles preferred.";

        var jobs = golden.Select(g => new Job(g.Title, g.Company, g.Location, "", g.Snippet, "", "golden_set")).ToList();

        // Deliberately NOT caught here: a dropped index means the eval itself is untrustworthy
        // this run, and that should crash loudly rather than silently score fewer jobs than exist.
        var scores = await new JobMatcher().ScoreJobsAsync(profile, target, jobs);

        var scoreByIndex = scores.Where(s => s.Index >= 0 && s.Index < jobs.Count)
                                 .ToDictionary(s => s.Index, s => s.FitScore);

        int tp = 0, tn = 0, fp = 0, fn = 0;

        Console.WriteLine($"{"Job",-46}{"You",-6}{"Brain",-7}{"Score",-7}Result");
        Console.WriteLine(new string('-', 86));
        for (int i = 0; i < golden.Length; i++)
        {
            int score = scoreByIndex.TryGetValue(i, out var sc) ? sc : 0;
            bool brain = score >= Threshold;
            bool you = golden[i].Relevant;

            string result =
                (you && brain) ? "agree (hit)" :
                (!you && !brain) ? "agree (skip)" :
                (!you && brain) ? "DISAGREE (false positive)" : "DISAGREE (missed)";
            if (you && brain) tp++; else if (!you && !brain) tn++; else if (!you && brain) fp++; else fn++;

            string name = $"{golden[i].Title} @ {golden[i].Company}";
            if (name.Length > 45) name = name[..45];
            Console.WriteLine($"{name,-46}{(you ? "yes" : "no"),-6}{(brain ? "yes" : "no"),-7}{score,-7}{result}");
        }

        int total = golden.Length;
        Console.WriteLine(new string('-', 86));
        Console.WriteLine($"Accuracy:  {(double)(tp + tn) / total:P0}  ({tp + tn}/{total} agree with your labels)");
        Console.WriteLine($"Precision: {((tp + fp) == 0 ? 0 : (double)tp / (tp + fp)):P0}  (when brain says relevant, how often it's right)");
        Console.WriteLine($"Recall:    {((tp + fn) == 0 ? 0 : (double)tp / (tp + fn)):P0}  (of jobs you marked relevant, how many it caught)");
    }
}
