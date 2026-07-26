using GenerativeAI.Microsoft;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Text;
using System.Text.Json;

/// <summary>
/// Drafts a tailored resume emphasis (summary + reordered/rephrased bullets pulled from the
/// candidate's real experience - never invented) and a short cover letter for one specific job.
/// Output is a DRAFT for the candidate to review and edit before use - this agent does not
/// apply anywhere and nothing it produces is sent to an employer automatically.
/// </summary>
public class DraftAgent
{
    private readonly AIAgent _draftAgent;

    public DraftAgent(string? geminiApiKey = null)
    {
        string geminiKey = geminiApiKey ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? throw new InvalidOperationException("GEMINI_API_KEY is not set.");
        IChatClient chat = new GenerativeAIChatClient(geminiKey, "gemini-2.5-flash-lite");

        _draftAgent = chat.AsAIAgent(
            name: "Drafter",
            instructions: """
            You tailor application materials for ONE specific job, using ONLY facts given to you
            about the candidate. Do not invent employers, technologies, metrics, or dates the
            candidate did not provide.

            Produce:
            1. tailoredSummary: 2-3 sentences repositioning the candidate's real background toward
               this specific job's stack and domain.
            2. emphasizedBullets: 4-6 bullets selected/rephrased from the candidate's REAL experience
               bullets (given to you below), reordered and lightly reworded to foreground whatever
               is most relevant to this job. Do not fabricate new bullets or numbers not present in
               the source bullets.
            3. coverLetter: 150-220 words, referencing the actual company and role title, explaining
               fit and (briefly, one sentence) that the candidate will need visa sponsorship and is
               open to relocating.

            Return ONLY JSON, no markdown:
            {"tailoredSummary":"...","emphasizedBullets":["...","..."],"coverLetter":"..."}
            """);
    }

    public async Task<DraftResult> DraftAsync(CandidateProfile profile, RankedJob job)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"JOB: {job.Title} at {job.Company}, {job.Location}");
        sb.AppendLine($"Why it matched: {job.Reason}");
        sb.AppendLine();
        sb.AppendLine($"CANDIDATE SUMMARY: {profile.Summary}");
        sb.AppendLine($"CANDIDATE SKILLS: {string.Join(", ", profile.Skills)}");
        sb.AppendLine("CANDIDATE REAL EXPERIENCE (only source of truth for bullets):");
        foreach (var exp in profile.Experience)
        {
            sb.AppendLine($"- {exp.Role} @ {exp.Company} ({exp.Dates})");
            foreach (var b in exp.Bullets)
                sb.AppendLine($"    * {b}");
        }

        var runOptions = new ChatClientAgentRunOptions(new ChatOptions { Temperature = 0.3f });
        string raw = (await _draftAgent.RunAsync(sb.ToString(), options: runOptions)).Text.Trim();
        if (raw.StartsWith("```"))
        {
            int nl = raw.IndexOf('\n');
            raw = raw[(nl + 1)..];
            if (raw.EndsWith("```")) raw = raw[..^3];
            raw = raw.Trim();
        }

        return JsonSerializer.Deserialize<DraftResult>(raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
