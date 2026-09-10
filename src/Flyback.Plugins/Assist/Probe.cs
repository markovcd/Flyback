using System.Text.Json.Nodes;
using Flyback.Core.Render;

namespace Flyback.Plugins.Assist;

/// <summary>
/// What a survey hands a model to find out what it will take, and how the answer is
/// read when it will not.
/// </summary>
/// <remarks>
/// None of this knows which provider is asking: whether a model accepts a picture is
/// settled by sending it one and seeing what comes back, so every adapter needs a
/// picture and a sound and they may as well be the same ones. Both are as small as
/// they can legally be, because a survey sends them once per model down a list.
/// </remarks>
public static class Probe
{
    /// <summary>A 1×1 PNG. The smallest thing that is legally a picture.</summary>
    /// <remarks>
    /// A fresh array each call rather than one shared: a byte array cannot be
    /// handed out and still be trusted, and this is cheap enough that saying so
    /// costs nothing.
    /// </remarks>
    public static byte[] Picture() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>
    /// A tenth of a second of 440 Hz, as a WAV.
    /// </summary>
    /// <remarks>
    /// A tone rather than silence, so a model that actually listens has something to
    /// find: a silent clip and a clip that was never decoded produce the same answer,
    /// which is the one answer a survey cannot use. Written through
    /// <see cref="WavWriter"/>, so this is not a second RIFF encoder.
    /// </remarks>
    public static byte[] Sound()
    {
        const int Rate = 8000;
        const int Tenth = Rate / 10;
        const double Hz = 440;

        // A quarter of full scale: loud enough to be found, and nowhere near a
        // level anything downstream has to think about.
        const float Peak = 0.25f;

        var samples = new float[Tenth];

        for (var i = 0; i < samples.Length; i++)
            samples[i] = (float)Math.Sin(2 * Math.PI * Hz * i / Rate) * Peak;

        using var buffer = new MemoryStream();

        WavWriter.Write(buffer, samples, Rate, channels: 1);

        return buffer.ToArray();
    }

    /// <summary>
    /// What an endpoint said went wrong, out of whatever it answered with.
    /// </summary>
    /// <remarks>
    /// Both providers put it in the same place and both are equally free not to — a
    /// gateway or an outage answers with HTML as readily. So the body itself is the
    /// fallback rather than a sentence apologising for it, since the unhelpful thing
    /// is usually the whole explanation.
    /// </remarks>
    public static string Detail(string body)
    {
        try
        {
            return JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>() ?? body;
        }
        catch (System.Text.Json.JsonException)
        {
            return body;
        }
    }

    /// <summary>
    /// The client a survey asks over, less whatever says who is asking.
    /// </summary>
    /// <param name="transport">
    /// A handler to send over instead of the network, which is how the surveys are
    /// tested. Not disposed with the client, because the caller is using it for more
    /// than one.
    /// </param>
    /// <remarks>
    /// The timeout is why this is shared: a survey walks a list of models asking each
    /// several questions, and the default hundred seconds cuts that off in the middle
    /// and reports a working endpoint as broken. Authentication is left to the caller,
    /// being the one part no two providers spell the same way.
    /// </remarks>
    public static HttpClient Client(HttpMessageHandler? transport)
    {
        var client = transport is null ? new HttpClient() : new HttpClient(transport, disposeHandler: false);

        client.Timeout = TimeSpan.FromMinutes(5);

        return client;
    }
}
