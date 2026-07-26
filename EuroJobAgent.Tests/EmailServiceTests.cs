using Xunit;

public class EmailServiceTests
{
    private static TrackedJob MakeJob(string title, string company, int score, bool isTarget = false) => new()
    {
        JobId = "ID1",
        Title = title,
        Company = company,
        Location = "Netherlands",
        Link = "https://example.com/job",
        FitScore = score,
        Reason = "reason",
        Missing = "none",
        IsTargetCompany = isTarget,
        Status = "New",
        FirstSeenUtc = "2026-01-01 00:00:00Z",
        TailoredSummary = "",
        EmphasizedBullets = [],
        CoverLetter = ""
    };

    [Fact]
    public void BuildDigestHtml_EscapesHtmlInJobFields()
    {
        var job = MakeJob("<script>alert(1)</script>", "Evil & Co", 90);

        string html = EmailService.BuildDigestHtml([job]);

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("Evil &amp; Co", html);
    }

    [Fact]
    public void BuildDigestHtml_ShowsTargetSponsorBadge_OnlyWhenFlagged()
    {
        var targetJob = MakeJob("Role A", "ABN AMRO", 90, isTarget: true);
        var normalJob = MakeJob("Role B", "Some Co", 90, isTarget: false);

        string htmlWithTarget = EmailService.BuildDigestHtml([targetJob]);
        string htmlWithoutTarget = EmailService.BuildDigestHtml([normalJob]);

        Assert.Contains("TARGET SPONSOR COMPANY", htmlWithTarget);
        Assert.DoesNotContain("TARGET SPONSOR COMPANY", htmlWithoutTarget);
    }

    [Fact]
    public void BuildDigestHtml_OrdersByFitScoreDescending()
    {
        var low = MakeJob("Low Score Role", "Co A", 40);
        var high = MakeJob("High Score Role", "Co B", 95);

        string html = EmailService.BuildDigestHtml([low, high]);

        int highIndex = html.IndexOf("High Score Role");
        int lowIndex = html.IndexOf("Low Score Role");
        Assert.True(highIndex < lowIndex, "Higher-scoring job should render first.");
    }

    [Fact]
    public void BuildDigestHtml_IncludesCountInHeading()
    {
        string html = EmailService.BuildDigestHtml([MakeJob("A", "Co", 80), MakeJob("B", "Co", 80)]);
        Assert.Contains("2 new match(es) found", html);
    }
}
