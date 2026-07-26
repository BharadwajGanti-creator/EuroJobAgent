using Xunit;

public class TrackerStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"tracker_test_{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private static TrackedJob MakeJob(string id, string title = "Test Role", string company = "Test Co") => new()
    {
        JobId = id,
        Title = title,
        Company = company,
        Location = "Netherlands",
        Link = $"https://example.com/{id}",
        FitScore = 80,
        Status = "New",
        FirstSeenUtc = "2026-01-01 00:00:00Z"
    };

    [Fact]
    public void Load_ReturnsEmptyDictionary_WhenFileDoesNotExist() =>
        Assert.Empty(TrackerStore.Load(_path));

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var tracker = new Dictionary<string, TrackedJob> { ["ABC123"] = MakeJob("ABC123") };
        TrackerStore.Save(tracker, _path);

        var loaded = TrackerStore.Load(_path);

        Assert.Single(loaded);
        Assert.Equal("Test Role", loaded["ABC123"].Title);
        Assert.Equal("Test Co", loaded["ABC123"].Company);
        Assert.Equal(80, loaded["ABC123"].FitScore);
    }

    [Fact]
    public void Mark_ExactId_UpdatesStatus()
    {
        TrackerStore.Save(new Dictionary<string, TrackedJob> { ["ABC123"] = MakeJob("ABC123") }, _path);

        int code = TrackerStore.Mark("ABC123", "Applied", _path);

        Assert.Equal(0, code);
        Assert.Equal("Applied", TrackerStore.Load(_path)["ABC123"].Status);
    }

    [Fact]
    public void Mark_UnambiguousPrefix_UpdatesStatus()
    {
        TrackerStore.Save(new Dictionary<string, TrackedJob> { ["ABC123XYZ"] = MakeJob("ABC123XYZ") }, _path);

        int code = TrackerStore.Mark("ABC1", "Interview", _path);

        Assert.Equal(0, code);
        Assert.Equal("Interview", TrackerStore.Load(_path)["ABC123XYZ"].Status);
    }

    [Fact]
    public void Mark_AmbiguousPrefix_FailsAndLeavesDataUnchanged()
    {
        var tracker = new Dictionary<string, TrackedJob>
        {
            ["ABC111"] = MakeJob("ABC111"),
            ["ABC222"] = MakeJob("ABC222")
        };
        TrackerStore.Save(tracker, _path);

        int code = TrackerStore.Mark("ABC", "Applied", _path);

        Assert.Equal(1, code);
        var reloaded = TrackerStore.Load(_path);
        Assert.Equal("New", reloaded["ABC111"].Status);
        Assert.Equal("New", reloaded["ABC222"].Status);
    }

    [Fact]
    public void Mark_NoMatch_ReturnsNonZero()
    {
        TrackerStore.Save(new Dictionary<string, TrackedJob> { ["ABC123"] = MakeJob("ABC123") }, _path);

        int code = TrackerStore.Mark("ZZZ", "Applied", _path);

        Assert.Equal(1, code);
    }
}
