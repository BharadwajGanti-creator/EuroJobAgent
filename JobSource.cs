using System.Text;
using System.Text.Json;

public class JoobleJobSource : IJobSource
{
    private static readonly HttpClient Http = new();
    private readonly string _key;

    public JoobleJobSource(string? apiKey = null)
    {
        _key = apiKey ?? Environment.GetEnvironmentVariable("JOOBLE_API_KEY")
            ?? throw new InvalidOperationException("JOOBLE_API_KEY is not set.");
    }

    public async Task<List<Job>> SearchAsync(string keywords, string location)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { keywords, location }), Encoding.UTF8, "application/json");

        var resp = await Http.PostAsync($"https://jooble.org/api/{_key}", content);
        resp.EnsureSuccessStatusCode();

        var parsed = JsonSerializer.Deserialize<JoobleResponse>(
            await resp.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        return (parsed.Jobs ?? [])
            .Select(j => new Job(j.Title, j.Company, j.Location, j.Link, Clean(j.Snippet), j.Salary ?? "", "Jooble"))
            .ToList();

        static string Clean(string? s) =>
            System.Text.RegularExpressions.Regex.Replace(
                System.Net.WebUtility.HtmlDecode(
                    System.Text.RegularExpressions.Regex.Replace(s ?? "", "<.*?>", " ")),
                @"\s+", " ").Trim();
    }

    private record JoobleResponse(int TotalCount, JoobleJob[] Jobs);
    private record JoobleJob(string Title, string Location, string Snippet, string? Salary,
                              string Source, string Type, string Link, string Company, string Updated);
}
