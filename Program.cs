using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

// ---------- `mark` mode: update a tracked job's status locally, before you commit ----------
if (args.Length > 0 && args[0] == "mark")
{
    if (args.Length < 3)
    {
        Console.WriteLine("Usage: dotnet run -- mark <jobId-or-prefix> <Status>");
        return 1;
    }
    return TrackerStore.Mark(args[1], args[2]);
}

// ---------- `eval` mode: precision/recall against golden_set.json ----------
if (args.Length > 0 && args[0] == "eval")
{
    await Eval.RunAsync();
    return 0;
}

// ---------- `dump` mode: fetch a fresh, UNLABELLED batch to grow golden_set.json from ----------
if (args.Length > 0 && args[0] == "dump")
{
    if (!File.Exists(AppConfig.CandidateProfilePath))
    {
        Console.WriteLine($"ERROR: {AppConfig.CandidateProfilePath} not found.");
        return 1;
    }
    var dumpProfile = JsonSerializer.Deserialize<CandidateProfile>(
        File.ReadAllText(AppConfig.CandidateProfilePath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    var dumpMatcher = new JobMatcher();
    IJobSource dumpSource = new JoobleJobSource();
    var seen = new HashSet<string>();
    var jobs = new List<Job>();

    foreach (var location in AppConfig.TargetLocations)
    {
        string target = $"Mid-level backend .NET/C# developer seeking visa-sponsored relocation " +
                         $"to {location}. Banking, insurance, and Microsoft-stack consultancy roles preferred.";
        string[] queries = await dumpMatcher.PlanQueriesAsync(dumpProfile, target);
        foreach (var q in queries)
            foreach (var job in await dumpSource.SearchAsync(q, location))
                if (seen.Add(job.Link)) jobs.Add(job);
    }

    File.WriteAllText("jobs_dump.json", JsonSerializer.Serialize(
        jobs.Select(j => new { j.Title, j.Company, j.Location, j.Snippet, relevant = false }),
        new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Wrote {jobs.Count} jobs to jobs_dump.json. Hand-label the \"relevant\" field, " +
                       "then move entries you're confident about into golden_set.json.");
    return 0;
}

// ---------- `cron` mode: the whole daily pipeline ----------
if (args.Length > 0 && args[0] == "cron")
    return await RunCronAsync();

// ---------- default: WEB MODE - live dashboard over tracker.json ----------
return RunWebMode();


async Task<int> RunCronAsync()
{
    EmailService? email = null;
    try
    {
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
        email = new EmailService();

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
                string html = EmailService.BuildDigestHtml(emailable);
                await email.SendDigestAsync($"EuroJobAgent: {emailable.Count} new match(es)", html);
                Console.WriteLine($"Sent digest email for {emailable.Count} job(s).");
            }
            catch (Exception ex)
            {
                // Deliberately not rethrown: results are already safely in tracker.json, and a
                // failed notification shouldn't be treated as severely as a failed pipeline run.
                Console.WriteLine($"WARNING: email send failed: {ex.Message}. Results are still saved in tracker.json.");
            }
        }
        else
        {
            Console.WriteLine("No jobs crossed the draft/email threshold this run - no email sent.");
        }

        return 0;
    }
    catch (Exception ex)
    {
        // Anything NOT already handled above (Jooble/Gemini exhausting retries, a malformed
        // candidate profile, an unreachable SendGrid host during the failure-alert send itself,
        // etc.) lands here. The run still needs to exit non-zero so GitHub Actions shows a red X -
        // but since nobody watches the Actions tab daily, also try to email about it directly.
        Console.WriteLine($"FATAL: unhandled exception in cron pipeline: {ex}");
        try
        {
            await (email ?? new EmailService()).SendFailureAlertAsync(ex.Message, ex.ToString());
        }
        catch (Exception alertEx)
        {
            Console.WriteLine($"WARNING: could not send failure-alert email either: {alertEx.Message}");
        }
        return 1;
    }
}

int RunWebMode()
{
    var builder = WebApplication.CreateBuilder(args);
    var app = builder.Build();

    app.UseDefaultFiles();   // serves wwwroot/index.html at "/"
    app.UseStaticFiles();

    app.MapGet("/api/tracker", () =>
    {
        var tracker = TrackerStore.Load();
        return Results.Json(tracker.Values.OrderByDescending(j => j.FitScore));
    });

    app.MapPost("/api/mark", (MarkRequest req) =>
    {
        int code = TrackerStore.Mark(req.JobId, req.Status);
        return code == 0
            ? Results.Json(new { ok = true })
            : Results.Json(new { ok = false, error = "Job not found or ambiguous prefix." }, statusCode: 400);
    });

    app.MapPost("/api/run", async () =>
    {
        // Reuses the exact same pipeline as `dotnet run -- cron`, triggered on demand from the
        // dashboard's "Run now" button, for local/interactive use. Drafting + email still apply
        // the same DraftThreshold gate, so clicking this won't spam your inbox.
        try
        {
            int code = await RunCronAsync();
            return code == 0
                ? Results.Json(new { ok = true })
                : Results.Json(new { ok = false }, statusCode: 500);
        }
        catch (Exception ex)
        {
            return Results.Json(new { ok = false, error = ex.Message }, statusCode: 500);
        }
    });

    app.Run();
    return 0;
}

record MarkRequest(string JobId, string Status);
