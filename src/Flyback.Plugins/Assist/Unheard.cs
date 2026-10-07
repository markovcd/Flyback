using System.Text.RegularExpressions;

namespace Flyback.Plugins.Assist;

/// <summary>
/// What a turn says when the person asks for the sound to be heard or measured and
/// this conversation has no ear: said by the host, since a model told it cannot hear
/// still reports levels it never measured.
/// </summary>
internal static partial class Unheard
{
    /// <summary>What the person is told, before the model says anything.</summary>
    /// <param name="earOffered">Whether the provider's settings have a switch that would let it hear.</param>
    public static string Told(bool earOffered) =>
        "This assistant cannot hear the sound or measure its loudness in this conversation."
        + (earOffered ? " 'Let it listen to the sound' in its settings turns that on." : string.Empty);

    /// <summary>What goes ahead of the message, for the model.</summary>
    public const string Note =
        "[From Flyback, not the person: you cannot hear the sound or measure its loudness in this "
        + "conversation, and the person has been told so. Say so too, and do not state a level or "
        + "describe how it sounds as though you had heard or measured it.]";

    /// <summary>Whether <paramref name="message"/> asks for the sound to be listened to or for a loudness.</summary>
    public static bool Asked(string message) => Asking().IsMatch(message);

    [GeneratedRegex(@"\b(listen|listens|listening|listened|hear|hears|hearing|heard|LUFS|loudness|dBFS|dBTP)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Asking();
}
