using System.Security.Cryptography;
using System.Text;

// ---------- Candidate ----------

public record ExperienceEntry(string Role, string Company, string Dates, string[] Bullets);

public record CandidateProfile(
    string[] Skills,
    double YearsOfExperience,
    string[] JobTitles,
    string[] Domains,
    string SeniorityLevel,
    string[] Education,
    string CurrentLocation,
    string[] TargetLocations,
    ExperienceEntry[] Experience,
    string Summary
);

// ---------- Raw job source ----------

public record Job(string Title, string Company, string Location, string Link, string Snippet, string Salary, string Source);

public interface IJobSource
{
    Task<List<Job>> SearchAsync(string keywords, string location);
}

// ---------- Matching ----------

public record Score(int Index, int FitScore, string Reason, string Missing);

public record RankedJob(
    string JobId, string Title, string Company, string Location, string Link,
    string Salary, int FitScore, string Reason, string Missing, bool IsTargetCompany
);

public record SearchTrace(string[] PlannedQueries, int Fetched, int Unique, int NewCount, List<RankedJob> NewMatches);

// ---------- Drafting ----------

public record DraftResult(string TailoredSummary, string[] EmphasizedBullets, string CoverLetter);

// ---------- Persistent tracker ----------

public class TrackedJob
{
    public string JobId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Location { get; set; } = "";
    public string Link { get; set; } = "";
    public string Salary { get; set; } = "";
    public int FitScore { get; set; }
    public string Reason { get; set; } = "";
    public string Missing { get; set; } = "";
    public bool IsTargetCompany { get; set; }
    public string Status { get; set; } = "New";
    public string FirstSeenUtc { get; set; } = "";
    public string TailoredSummary { get; set; } = "";
    public string[] EmphasizedBullets { get; set; } = [];
    public string CoverLetter { get; set; } = "";
}

public static class JobIdentity
{
    // Jooble does not give a stable numeric ID, so the job's link is the closest thing to one.
    // Hashed (not stored raw) purely to keep tracker.json keys short and uniform.
    public static string From(string link)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(link));
        return Convert.ToHexString(bytes)[..16];
    }
}
