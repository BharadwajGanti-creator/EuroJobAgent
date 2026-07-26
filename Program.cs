using System.Text.Json;

// ---------- `mark` mode: update a tracked job's status locally, before you commit ----------
// Usage: dotnet run -- mark <jobId-or-prefix> <Status>
if (args.Length > 0 && args[0] == "mark")
{
    if (args.Length < 3)
    {
        Console.WriteLine("Usage: dotnet run -- mark <jobId-or-prefix> <Status>");
        return 1;
    }
    return TrackerStore.Mark(args[1], args[2]);
}

// ---------- `cron` mode: the whole daily pipeline ----------
if (args.Length == 0 || args[0] != "cron")
{
    Console.WriteLine("Usage: dotnet run -- cron        (run the full search/draft/email pipeline)");
    Console.WriteLine("       dotnet run -- mark <id> <Status>   (update a tracked job's status)");
    return 1;
}

var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

if (!File.Exists(AppConfig.CandidateProfilePath))
{
    Console.WriteLine($"ERROR: {AppConfig.CandidateProfilePath} not found. " +
                       "In CI this is written from the CANDIDATE_PROFILE_JSON secret before this step runs.");
    return 1;
}

var profile = JsonSerializer.Deserialize<CandidateProfile>(
    File.ReadAllText(AppConfig.CandidateProfilePath), jsonOpts)!;

var tracker = TrackerStore.Load();
var alreadySeen = tracker.Keys.ToHashSet();

var matcher = new JobMatcher();
IJobSource jobSource = new JoobleJobSource();

Console.WriteLine($"Tracker currently holds {tracker.Count} job(s). Searching: {string.Join(", ", AppConfig.TargetLocations)}...");

var trace = await SearchService.RunAsync(profile, matcher, jobSource, alreadySeen);

Console.WriteLine($"Planned {trace.PlannedQueries.Length} queries, fetched {trace.Fetched}, " +
                   $"{trace.Unique} unique, {trace.NewCount} new (not seen in a previous run).");

var newlyTracked = new List<TrackedJob>();
var drafter = new DraftAgent();
string nowUtc = DateTime.UtcNow.ToString("u");

foreach (var job in trace.NewMatches)
{
    var tracked = new TrackedJob
    {
        JobId = job.JobId,
        Title = job.Title,
        Company = job.Company,
        Location = job.Location,
        Link = job.Link,
        Salary = job.Salary,
        FitScore = job.FitScore,
        Reason = job.Reason,
        Missing = job.Missing,
        IsTargetCompany = job.IsTargetCompany,
        Status = "New",
        FirstSeenUtc = nowUtc
    };

    if (job.FitScore >= AppConfig.DraftThreshold)
    {
        try
        {
            var draft = await drafter.DraftAsync(profile, job);
            tracked.TailoredSummary = draft.TailoredSummary;
            tracked.EmphasizedBullets = draft.EmphasizedBullets;
            tracked.CoverLetter = draft.CoverLetter;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: drafting failed for {job.JobId} ({job.Title} @ {job.Company}): {ex.Message}");
            tracked.TailoredSummary = "(Draft generation failed - review this job manually.)";
        }
    }

    tracker[job.JobId] = tracked;
    newlyTracked.Add(tracked);
}

TrackerStore.Save(tracker);
Console.WriteLine($"tracker.json now holds {tracker.Count} job(s) - remember this file needs to be committed.");

var emailable = newlyTracked.Where(j => j.FitScore >= AppConfig.DraftThreshold).ToList();
if (emailable.Count > 0)
{
    try
    {
        var email = new EmailService();
        string html = EmailService.BuildDigestHtml(emailable);
        await email.SendDigestAsync($"EuroJobAgent: {emailable.Count} new match(es)", html);
        Console.WriteLine($"Sent digest email for {emailable.Count} job(s).");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"WARNING: email send failed: {ex.Message}. Results are still saved in tracker.json.");
    }
}
else
{
    Console.WriteLine("No jobs crossed the draft/email threshold this run - no email sent.");
}

return 0;
