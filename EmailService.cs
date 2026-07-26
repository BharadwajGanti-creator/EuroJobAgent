using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

/// <summary>
/// Thin wrapper over SendGrid's v3 Mail Send API (POST /v3/mail/send). Raw HttpClient rather than
/// the SendGrid SDK - one documented REST call, no extra NuGet dependency to version-pin.
/// Contract verified against https://www.twilio.com/docs/sendgrid/api-reference/mail-send/mail-send
/// </summary>
public class EmailService
{
    private static readonly HttpClient Http = new() { BaseAddress = new Uri("https://api.sendgrid.com") };
    private readonly string _apiKey;
    private readonly string _fromEmail;
    private readonly string _toEmail;

    public EmailService(string? apiKey = null, string? fromEmail = null, string? toEmail = null)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("SENDGRID_API_KEY")
            ?? throw new InvalidOperationException("SENDGRID_API_KEY is not set.");
        _fromEmail = fromEmail ?? Environment.GetEnvironmentVariable("EMAIL_FROM")
            ?? throw new InvalidOperationException("EMAIL_FROM is not set (must be a SendGrid-verified sender).");
        _toEmail = toEmail ?? Environment.GetEnvironmentVariable("EMAIL_TO")
            ?? throw new InvalidOperationException("EMAIL_TO is not set.");
    }

    public async Task SendDigestAsync(string subject, string htmlBody)
    {
        var payload = new
        {
            personalizations = new[] { new { to = new[] { new { email = _toEmail } } } },
            from = new { email = _fromEmail, name = "EuroJobAgent" },
            subject,
            content = new[] { new { type = "text/html", value = htmlBody } }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "/v3/mail/send")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"SendGrid returned {(int)resp.StatusCode}: {body}");
        }
    }

    public static string BuildDigestHtml(List<TrackedJob> newJobs)
    {
        var sb = new StringBuilder();
        sb.Append($"<h2>{newJobs.Count} new match(es) found</h2>");

        foreach (var j in newJobs.OrderByDescending(x => x.FitScore))
        {
            var targetBadge = j.IsTargetCompany ? " <b style=\"color:#1F3864\">[TARGET SPONSOR COMPANY]</b>" : "";
            sb.Append($"""
                <div style="border:1px solid #ddd;border-radius:8px;padding:14px;margin-bottom:14px;font-family:Arial,sans-serif;">
                  <div style="font-size:16px;font-weight:bold;">{Escape(j.Title)} — {Escape(j.Company)}{targetBadge}</div>
                  <div style="color:#666;margin:2px 0 8px;">{Escape(j.Location)} · Fit score: {j.FitScore}/100 · {Escape(j.Salary)}</div>
                  <div><b>Why it matched:</b> {Escape(j.Reason)}</div>
                  <div><b>Missing:</b> {Escape(j.Missing)}</div>
                  <div style="margin-top:8px;"><b>Tailored summary:</b> {Escape(j.TailoredSummary)}</div>
                  <ul>{string.Join("", j.EmphasizedBullets.Select(b => $"<li>{Escape(b)}</li>"))}</ul>
                  <div style="margin-top:8px;"><b>Draft cover letter:</b><br/>{Escape(j.CoverLetter).Replace("\n", "<br/>")}</div>
                  <div style="margin-top:8px;"><a href="{j.Link}">View posting →</a></div>
                  <div style="margin-top:6px;color:#999;font-size:12px;">Job ID: {j.JobId} — review this draft before using it, then apply manually and run `dotnet run -- mark {j.JobId} Applied`.</div>
                </div>
                """);
        }
        return sb.ToString();
    }

    private static string Escape(string? s) =>
        (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
