/// <summary>
/// Minimal exponential-backoff retry, no external dependency (deliberately not pulling in Polly
/// for three call sites). Retries on any exception except the ones explicitly excluded - a
/// dropped-index scoring failure should NOT be retried here, since re-asking the same prompt
/// is unlikely to change the LLM's behaviour; that failure is handled explicitly by its caller
/// instead (see JobMatcher.ScoreJobsAsync and Program.cs's cron mode).
/// </summary>
public static class Retry
{
    public static async Task<T> WithBackoffAsync<T>(
        Func<Task<T>> action, int maxAttempts = 3, int initialDelayMs = 1000, string? label = null)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt == maxAttempts) break;
                int delay = initialDelayMs * (int)Math.Pow(2, attempt - 1);
                Console.WriteLine($"WARNING: {(label ?? "call")} failed on attempt {attempt}/{maxAttempts} " +
                                   $"({ex.GetType().Name}: {ex.Message}). Retrying in {delay}ms.");
                await Task.Delay(delay);
            }
        }
        throw new InvalidOperationException(
            $"{(label ?? "Call")} failed after {maxAttempts} attempts.", last);
    }
}
