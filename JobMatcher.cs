using GenerativeAI.Microsoft;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Text;
using System.Text.Json;

public class JobMatcher
{
    private readonly AIAgent _queryAgent;
    private readonly AIAgent _matchAgent;

    public JobMatcher(string? geminiApiKey = null)
    {
        string geminiKey = geminiApiKey ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? throw new InvalidOperationException("GEMINI_API_KEY is not set.");
        IChatClient chat = new GenerativeAIChatClient(geminiKey, "gemini-2.5-flash-lite");

        _queryAgent = chat.AsAIAgent(
            name: "QueryPlanner",
            instructions: """
            You plan job-search queries for a specific country/location. Given a candidate profile
            and a target description (which includes the country/city to search in), output 4 short
            search queries likely to surface relevant .NET/C# backend roles in that location.
            Mix general queries ("C# developer", ".NET backend developer") with domain-flavoured ones
            (e.g. "C# developer bank", ".NET developer insurance") since the candidate's background
            is financial services.
            Return ONLY a JSON array of strings, no markdown.
            """);

        _matchAgent = chat.AsAIAgent(
            name: "Matcher",
            instructions: """
            You score how well each job fits the candidate: a mid-level backend software engineer
            with a C#/.NET, Angular, and SQL Server background in banking/insurance systems, currently
            seeking a visa-sponsored relocation.
            For each job return: its index, fitScore (0-100), a one-line reason, and the top missing
            skill ("none" if none).

            STRONG FIT (score 70-100): individual-contributor backend roles built on C#/.NET
            (ASP.NET Core, .NET Framework, Web API), at mid or medior level, ideally in banking,
            insurance, or financial-services-adjacent domains.

            DECENT FIT (score 40-69): other backend languages (Java, Python, Go) at a bank/insurer/
            fintech, OR a .NET role that looks senior/lead but individual-contributor, OR a .NET role
            with an unclear domain.

            NOT RELEVANT (score 0-39), regardless of "develop / engineer / build" language:
            - Management or above-senior IC: Manager, Lead, Principal, Staff, Director, Head, VP.
            - Test/quality roles: QA, SDET, "in Test", test automation.
            - Infrastructure/operations: SRE, DevOps-only, Network, pure Cloud ops with no app dev.
            - Frontend-only roles with no backend component.
            - Roles requiring a language the candidate doesn't have (unless C#/.NET is listed).

            Judge by ROLE TYPE, TECH STACK, and SENIORITY, not by buzzwords alone.

            Return ONLY a JSON array, no markdown:
            [{"index":0,"fitScore":82,"reason":"...","missing":"..."}]
            """);
    }

    public async Task<string[]> PlanQueriesAsync(CandidateProfile profile, string target) =>
        ParseJson<string[]>(await Retry.WithBackoffAsync(
            () => RunAndClean(_queryAgent, $"""
            Skills: {string.Join(", ", profile.Skills)}
            Seniority: {profile.SeniorityLevel}
            Target: {target}
            """),
            label: "Gemini QueryPlanner call"));

    /// <summary>
    /// Throws if the LLM's JSON response drops any job index - deliberately as strict as v1's
    /// matcher ("scores incomplete - don't trust the eval"). This method is the single source of
    /// truth for score integrity; it is used both by Eval.cs (where a dropped index should crash
    /// loudly, since a silently-incomplete eval run is worse than no eval run) and by the cron
    /// pipeline (Program.cs), which explicitly catches this specific exception and degrades
    /// gracefully instead - see the comment at that call site for why cron mode needs different
    /// behaviour than eval mode. The leniency lives at the call site, not here.
    /// </summary>
    public async Task<Score[]> ScoreJobsAsync(CandidateProfile profile, string target, List<Job> jobs)
    {
        if (jobs.Count == 0) return [];

        var sb = new StringBuilder();
        sb.AppendLine($"Candidate skills: {string.Join(", ", profile.Skills)}");
        sb.AppendLine($"Seniority: {profile.SeniorityLevel}");
        sb.AppendLine($"Target: {target}\n");
        sb.AppendLine("Jobs:");
        for (int i = 0; i < jobs.Count; i++)
        {
            sb.AppendLine($"{i}. {jobs[i].Title} @ {jobs[i].Company} ({jobs[i].Location})");
            if (!string.IsNullOrWhiteSpace(jobs[i].Snippet))
                sb.AppendLine($"   Description: {jobs[i].Snippet}");
        }

        var scores = ParseJson<Score[]>(await Retry.WithBackoffAsync(
            () => RunAndClean(_matchAgent, sb.ToString()),
            label: "Gemini Matcher call"));

        var returned = scores.Select(s => s.Index).ToHashSet();
        var missing = Enumerable.Range(0, jobs.Count).Where(i => !returned.Contains(i)).ToList();
        if (missing.Count > 0)
            throw new MatcherIncompleteException(
                $"Matcher dropped {missing.Count} of {jobs.Count} job(s): indices [{string.Join(", ", missing)}].",
                missing, scores);

        return scores;
    }

    private static async Task<string> RunAndClean(AIAgent agent, string prompt)
    {
        var runOptions = new ChatClientAgentRunOptions(new ChatOptions { Temperature = 0f });
        string raw = (await agent.RunAsync(prompt, options: runOptions)).Text.Trim();
        if (raw.StartsWith("```"))
        {
            int nl = raw.IndexOf('\n');
            raw = raw[(nl + 1)..];
            if (raw.EndsWith("```")) raw = raw[..^3];
            raw = raw.Trim();
        }
        return raw;
    }

    private static T ParseJson<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}
