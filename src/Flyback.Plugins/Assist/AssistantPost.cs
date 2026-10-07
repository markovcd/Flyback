namespace Flyback.Plugins.Assist;

/// <summary>
/// One request to a provider, retried where the endpoint asked to be. Where it says
/// how long to wait, and how it words a refusal, are the provider's to read.
/// </summary>
public static class AssistantPost
{
    /// <summary>How many times one request may be sent before it is given up on.</summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// The longest a refusal is waited out: a minute, so a tokens-per-minute limit
    /// is ridden out. One that resets beyond it is a quota, and no amount of
    /// waiting is the answer to a quota.
    /// </summary>
    public static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(60);

    private static readonly AsyncLocal<Action<TimeSpan, int>?> WaitingAsyncLocal = new();

    /// <summary>
    /// Told each wait as it starts, with the status that asked for it, by whatever
    /// runs the turn this request is part of.
    /// </summary>
    /// <remarks>Flows with the request rather than through the contract, so a provider passes nothing on.</remarks>
    internal static Action<TimeSpan, int>? Waiting
    {
        get => WaitingAsyncLocal.Value;
        set => WaitingAsyncLocal.Value = value;
    }

    /// <summary>Tells <see cref="Waiting"/> that a wait of <paramref name="wait"/> is starting.</summary>
    internal static void Tell(TimeSpan wait, int status) => Waiting?.Invoke(wait, status);

    /// <summary>Whether a refusal with this status is worth sending again.</summary>
    /// <remarks>
    /// 429 is the one that matters: a rate limit on a conversation that resends a
    /// large stable briefing every request is an ordinary event, and the endpoint
    /// usually says how long it wants. The 5xx are here because a gateway that is
    /// briefly unwell says so twice as often as it means it, and 503 routinely means
    /// a model is overloaded, which is a queue rather than a fault. Everything else
    /// in 4xx will still be wrong in a second.
    /// </remarks>
    public static bool Retryable(int status) =>
        status is 408 or 429 or 500 or 502 or 503 or 504 or 529;

    /// <summary>Sends <paramref name="body"/> as JSON over <paramref name="transport"/>, and gives back what came back.</summary>
    /// <param name="waitAsked">How long the refusal asked to be left alone, or null where it did not say.</param>
    /// <param name="complaint">The refusal as a sentence, from its status and body.</param>
    /// <exception cref="HttpRequestException">Refused, and not worth waiting out.</exception>
    public static async Task<string> Send(
        IAssistantTransport transport,
        Uri endpoint,
        string body,
        Func<AssistantResponse, TimeSpan?> waitAsked,
        Func<int, string, string> complaint,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(waitAsked);
        ArgumentNullException.ThrowIfNull(complaint);

        for (var attempt = 1; ; attempt++)
        {
            var response = await transport.Send(endpoint, body, cancel).ConfigureAwait(false);

            if (response.Succeeded) return response.Body;

            var wait = waitAsked(response) ?? Backoff(attempt);

            // A quota is told at once rather than sat on.
            if (attempt >= MaxAttempts || !Retryable(response.Status) || wait > LongestWait)
                throw new HttpRequestException(complaint(response.Status, response.Body));

            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;

            Tell(wait, response.Status);

            await Task.Delay(wait, cancel).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// What to wait when the endpoint refused without saying how long for. Doubling
    /// from a second, which spends about fifteen across the attempts.
    /// </summary>
    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
}
