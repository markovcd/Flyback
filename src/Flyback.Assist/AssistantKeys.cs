using Flyback.Plugins.Secrets;
using Flyback.Plugins.Settings;

namespace Flyback.Assist;

/// <summary>An assistant's key: the client that carries it, and where it is bound to go.</summary>
internal static class AssistantKeys
{
    /// <summary>What <paramref name="assistant"/> sends over, configured as <paramref name="values"/>.</summary>
    /// <param name="network">Where signed requests go; the network unless a test says otherwise.</param>
    public static IKeyedClient Client(this Credentials credentials, IPatchAssistant assistant, SettingValues values, HttpMessageHandler? network = null) =>
        credentials.Client(assistant.Id, assistant.Credential, OriginOf(assistant, values), network);

    /// <summary>The origin of what <paramref name="assistant"/> sends to configured as <paramref name="values"/>, or null where that is no address.</summary>
    public static string? OriginOf(IPatchAssistant assistant, SettingValues values)
    {
        try
        {
            return assistant.Endpoint(values) is { IsAbsoluteUri: true } address ? KeyedClient.OriginOf(address) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Why the key in <paramref name="config"/> will not be sent where <paramref name="assistant"/>
    /// is set to send, or null where it will.
    /// </summary>
    public static string? Elsewhere(IPatchAssistant assistant, AssistantConfig config)
    {
        var now = OriginOf(assistant, config.Values);

        if (!config.Client.HasKey)
        {
            var variable = assistant.Credential.EnvironmentVariable;

            if (string.IsNullOrWhiteSpace(variable) || Credentials.Blank(Environment.GetEnvironmentVariable(variable)) is not { } exported)
                return null;

            if (KeySafety.Refused(exported) is { } why) return $"{variable} is not sent: {why}";

            return KeySafety.Cleartext(now)
                ? $"{variable} is not sent to {now}: over plain http it would cross the network readable. "
                  + "A server that takes any value as a key can be given one here."
                : null;
        }

        if (config.Client.Origin is not { } bound) return null;

        return now is null || string.Equals(now, bound, StringComparison.Ordinal)
            ? null
            : $"The key was entered for {bound}, and this is set to send to {now}. Enter it again to send it there.";
    }
}
