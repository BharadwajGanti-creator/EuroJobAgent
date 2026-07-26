using Xunit;

public class JobIdentityTests
{
    [Fact]
    public void SameLink_ProducesSameId()
    {
        string link = "https://jooble.org/jdp/12345";
        Assert.Equal(JobIdentity.From(link), JobIdentity.From(link));
    }

    [Fact]
    public void DifferentLinks_ProduceDifferentIds()
    {
        string a = JobIdentity.From("https://jooble.org/jdp/12345");
        string b = JobIdentity.From("https://jooble.org/jdp/67890");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Id_Is16HexCharacters()
    {
        string id = JobIdentity.From("https://jooble.org/jdp/12345");
        Assert.Equal(16, id.Length);
        Assert.Matches("^[0-9A-F]{16}$", id);
    }

    [Fact]
    public void TrailingTrackingParam_ChangesId()
    {
        // Documents a known limitation (see README): the ID is a hash of the exact URL, so a
        // posting re-served under a different tracking querystring is treated as a new job.
        string a = JobIdentity.From("https://jooble.org/jdp/12345");
        string b = JobIdentity.From("https://jooble.org/jdp/12345?utm=abc");
        Assert.NotEqual(a, b);
    }
}
