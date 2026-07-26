using Xunit;

public class TargetCompanyMatcherTests
{
    private static readonly string[] Targets =
        ["ABN AMRO", "Rabobank", "NN Group", "Exact", "Info Support"];

    [Theory]
    [InlineData("ABN AMRO Bank N.V.", true)]
    [InlineData("abn amro bank n.v.", true)]        // case-insensitive
    [InlineData("Rabobank Nederland", true)]
    [InlineData("Info Support B.V.", true)]
    [InlineData("Storyteq", false)]
    [InlineData("Zscaler", false)]
    public void IsTarget_MatchesExpected(string company, bool expected) =>
        Assert.Equal(expected, TargetCompanyMatcher.IsTarget(company, Targets));

    [Fact]
    public void ApplyBoost_AddsFixedAmount_WhenTarget()
    {
        int boosted = TargetCompanyMatcher.ApplyBoost(fitScore: 70, isTarget: true);
        Assert.Equal(70 + TargetCompanyMatcher.BoostAmount, boosted);
    }

    [Fact]
    public void ApplyBoost_LeavesScoreUnchanged_WhenNotTarget()
    {
        int boosted = TargetCompanyMatcher.ApplyBoost(fitScore: 70, isTarget: false);
        Assert.Equal(70, boosted);
    }

    [Fact]
    public void ApplyBoost_NeverExceedsMax()
    {
        int boosted = TargetCompanyMatcher.ApplyBoost(fitScore: 95, isTarget: true);
        Assert.Equal(TargetCompanyMatcher.MaxScore, boosted);
    }

    [Fact]
    public void ApplyBoost_AtExactlyMax_StaysAtMax()
    {
        int boosted = TargetCompanyMatcher.ApplyBoost(fitScore: 100, isTarget: true);
        Assert.Equal(100, boosted);
    }
}
