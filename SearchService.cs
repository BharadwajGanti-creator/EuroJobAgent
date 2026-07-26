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

        Score[] scores;
        try
        {
            scores = await matcher.ScoreJobsAsync(profile, scoringTarget, jobs);
        }
        catch (MatcherIncompleteException ex)
        {
            // Eval.cs deliberately lets this crash - an incomplete eval is untrustworthy.
            // The unattended daily cron pipeline needs different behaviour: one bad LLM response
            // shouldn't zero out an entire day's run. Recover here, in the one place that still
            // has the original `jobs` list needed to patch it, and log loudly so it's visible in
            // the Actions log rather than silently swallowed.
            Console.WriteLine($"WARNING: {ex.Message} Defaulting dropped indices to fitScore 0 for this cron run.");

            // Re-run isn't attempted here (Retry already covers transient failures inside the
            // matcher call itself) - this branch is specifically for a malformed-but-successful
            // LLM response, which a retry of the same prompt is unlikely to fix.
            var combined = ex.PartialScores.ToList();
            foreach (var i in ex.MissingIndices)
                combined.Add(new Score(i, 0, "Not scored by matcher (dropped response)", "unknown"));
            scores = combined.ToArray();
        }

        var ranked = scores
            .Where(s => s.Index >= 0 && s.Index < jobs.Count)
            .Select(s =>
            {
                var j = jobs[s.Index];
                string jobId = JobIdentity.From(j.Link);
                bool isTarget = TargetCompanyMatcher.IsTarget(j.Company, AppConfig.TargetCompanies);
                int boosted = TargetCompanyMatcher.ApplyBoost(s.FitScore, isTarget);
                return new RankedJob(jobId, j.Title, j.Company, j.Location, j.Link, j.Salary,
                                      boosted, s.Reason, s.Missing, isTarget);
            })
            .OrderByDescending(r => r.FitScore)
            .ToList();

        var newOnly = ranked.Where(r => !alreadySeenJobIds.Contains(r.JobId)).ToList();

        return new SearchTrace(allQueries.ToArray(), fetched, jobs.Count, newOnly.Count, newOnly);
    }
}
