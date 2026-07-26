/// <summary>
/// Pure, deterministic logic pulled out of SearchService specifically so it's unit-testable
/// without touching the LLM agents. The LLM only judges role/stack/domain fit; whether a company
/// is a known visa sponsor is never left to it.
/// </summary>
public static class TargetCompanyMatcher
{
    public const int BoostAmount = 15;
    public const int MaxScore = 100;

    public static bool IsTarget(string company, string[] targetCompanies) =>
        targetCompanies.Any(c => company.Contains(c, StringComparison.OrdinalIgnoreCase));

    public static int ApplyBoost(int fitScore, bool isTarget) =>
        isTarget ? Math.Min(MaxScore, fitScore + BoostAmount) : fitScore;
}
