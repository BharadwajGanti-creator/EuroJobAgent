public static class AppConfig
{
    // Countries currently being actively worked, per the candidate's job-search strategy.
    // Netherlands first (highly-skilled-migrant route, no going-rate table); add "Germany"
    // here later once the Blue Card push starts - the pipeline loops over whatever's listed.
    public static readonly string[] TargetLocations =
        (Environment.GetEnvironmentVariable("TARGET_LOCATIONS") ?? "Netherlands")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // IND-recognised-sponsor candidates identified during research. Deterministic substring
    // match, not left to the LLM to "know" who sponsors visas.
    public static readonly string[] TargetCompanies =
    [
        "ABN AMRO", "Rabobank", "NN Group", "Nationale-Nederlanden", "Exact", "AFAS",
        "Info Support", "Sogeti", "Capgemini", "Ordina", "Sopra Steria", "Ravecruitment"
    ];

    // Only draft + email jobs at or above this fit score, to control Gemini + SendGrid usage
    // on noise. Raise this if the digest gets too busy, lower it if too quiet.
    public static readonly int DraftThreshold =
        int.TryParse(Environment.GetEnvironmentVariable("DRAFT_THRESHOLD"), out var t) ? t : 60;

    public const string CandidateProfilePath = "candidate_profile.json";
}
