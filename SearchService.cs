public static class SearchService
{
    /// <summary>
    /// Full pipeline for one run: plan queries per target location, fetch, dedupe within the run,
    /// score, apply the deterministic target-company boost, then hand back only jobs the tracker
    /// hasn't seen before (tracker-level dedupe happens in Program.cs, which owns tracker.json).
    /// </summary>
    public static async Task<SearchTrace> RunAsync(
        CandidateProfile profile, JobMatcher matcher, IJobSource source,
        HashSet<string> alreadySeenJobIds)
    {
        var allQueries = new List<string>();
        var seenLinks = new HashSet<string>();
        var jobs = new List<Job>();
        int fetched = 0;

        foreach (var location in AppConfig.TargetLocations)
        {
            string target = $"Mid-level backend .NET/C# developer seeking visa-sponsored relocation " +
                             $"to {location}. Banking, insurance, and Microsoft-stack consultancy roles preferred.";

            string[] queries = await matcher.PlanQueriesAsync(profile, target);
            allQueries.AddRange(queries.Select(q => $"{q} ({location})"));

            foreach (var q in queries)
            {
                foreach (var job in await source.SearchAsync(q, location))
                {
                    fetched++;
                    if (seenLinks.Add(job.Link)) jobs.Add(job);
                }
            }
        }

        string scoringTarget = "Mid-level backend .NET/C# developer seeking visa-sponsored relocation " +
                               $"to: {string.Join(" / ", AppConfig.TargetLocations)}. " +
                               "Banking, insurance, and Microsoft-stack consultancy roles preferred.";
        var scores = await matcher.ScoreJobsAsync(profile, scoringTarget, jobs);

        var ranked = scores
            .Where(s => s.Index >= 0 && s.Index < jobs.Count)
            .Select(s =>
            {
                var j = jobs[s.Index];
                string jobId = JobIdentity.From(j.Link);
                bool isTarget = AppConfig.TargetCompanies.Any(c =>
                    j.Company.Contains(c, StringComparison.OrdinalIgnoreCase));
                // Deterministic boost for confirmed/likely sponsor companies - capped at 100.
                int boosted = isTarget ? Math.Min(100, s.FitScore + 15) : s.FitScore;
                return new RankedJob(jobId, j.Title, j.Company, j.Location, j.Link, j.Salary,
                                      boosted, s.Reason, s.Missing, isTarget);
            })
            .OrderByDescending(r => r.FitScore)
            .ToList();

        var newOnly = ranked.Where(r => !alreadySeenJobIds.Contains(r.JobId)).ToList();

        return new SearchTrace(allQueries.ToArray(), fetched, jobs.Count, newOnly.Count, newOnly);
    }
}
