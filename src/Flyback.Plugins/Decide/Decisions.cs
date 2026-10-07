using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;

namespace Flyback.Plugins.Decide;

/// <summary>
/// The one door a feature asks a decision model through: the chosen model, configured, its
/// files in place, with a deadline. Where any of that is missing <see cref="Ask"/> answers
/// null and the feature carries on as it would with no model at all.
/// </summary>
/// <param name="models">Every model installed.</param>
/// <param name="settings">Which one is chosen and what each is set to.</param>
/// <param name="credentials">Where a hosted model's key comes from.</param>
/// <param name="store">Where models' files are kept.</param>
internal sealed class Decisions(
    IReadOnlyList<IDecisionModel> models,
    DecisionSettings settings,
    Credentials credentials,
    ModelStore store,
    HttpMessageHandler? network = null)
{
    /// <summary>How long a feature waits for an answer before carrying on without one.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    /// <summary>No model: every question answers null.</summary>
    public static Decisions None { get; } = new([], new DecisionSettings { Model = DecisionSettings.Off }, new Credentials(null), new ModelStore(null));

    public Decisions(PluginCatalog plugins, DecisionSettings settings, Credentials credentials, ModelStore store)
        : this(plugins.DecisionModels, settings, credentials, store)
    {
    }

    public IReadOnlyList<IDecisionModel> Models => models;

    public DecisionSettings Settings => settings;

    public Credentials Credentials => credentials;

    public ModelStore Store => store;

    /// <summary>The account a model's key is filed under, apart from any assistant's.</summary>
    public static string Account(IDecisionModel model) => "decision." + model.Id;

    /// <summary>
    /// The model a question goes to: the one chosen, or where nobody has chosen, the
    /// likeliest installed model that sends nothing anywhere. Null where decisions are off.
    /// </summary>
    public IDecisionModel? Chosen => settings.Model switch
    {
        DecisionSettings.Off => null,
        null or "" => models
            .Where(m => m.Credential is null && Quietly(() => m.Endpoint(settings.Of(m.Id))) is null)
            .OrderByDescending(m => m.Priority)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .FirstOrDefault(),
        var id => models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal)),
    };

    /// <summary>The model <paramref name="id"/> names, or null where none is installed by it.</summary>
    public IDecisionModel? Model(string id) => models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

    /// <summary><paramref name="model"/> as it is set, sending over a transport that carries its key.</summary>
    public DecisionConfig Config(IDecisionModel model)
    {
        var values = settings.Of(model.Id);
        var origin = Quietly(() => model.Endpoint(values)) is { IsAbsoluteUri: true } address ? KeyedTransport.OriginOf(address) : null;

        return new DecisionConfig(credentials.Transport(Account(model), model.Credential, origin, network), values, store.FolderOf(model));
    }

    /// <summary>Why the chosen model cannot answer now, or null when it can.</summary>
    public string? Unavailable() => Chosen is { } model ? Unavailable(model) : NotChosen();

    /// <summary>Why <paramref name="model"/> cannot answer now, or null when it can.</summary>
    public string? Unavailable(IDecisionModel model)
    {
        if (!store.Prepared(model))
            return $"{model.Name} is not downloaded yet ({Megabytes(store.Missing(model))}).";

        try
        {
            return model.Unavailable(Config(model));
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Why the last question went unanswered, or null after one that was answered.</summary>
    public string? Problem { get; private set; }

    /// <summary>
    /// The chosen model's answer, or null where there is no model, it cannot answer, it failed
    /// or it missed <see cref="Deadline"/>. Never throws but for <paramref name="cancel"/>.
    /// </summary>
    public async Task<Decision?> Ask(DecisionRequest request, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Chosen is not { } model)
        {
            Problem = NotChosen();
            return null;
        }

        if ((Unavailable(model) ?? request.Problem()) is { } why)
        {
            Problem = why;
            return null;
        }

        using var late = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        late.CancelAfter(Deadline);

        try
        {
            // Off the caller's thread: a model that runs here may work before its first await.
            var config = Config(model);
            var decision = await Task.Run(() => model.DecideAsync(request, config, late.Token), late.Token).ConfigureAwait(false);
            Problem = null;
            return decision;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            Problem = $"{model.Name} did not answer within {Deadline.TotalSeconds:0} seconds.";
            return null;
        }
        catch (Exception ex)
        {
            Problem = $"{model.Name}: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// <paramref name="model"/>'s answer, or the reason it has none as an exception: for the
    /// command line, where the reason is the output.
    /// </summary>
    /// <exception cref="InvalidOperationException">The model cannot answer, or the request is not one to send.</exception>
    public async Task<Decision> Decide(IDecisionModel model, DecisionRequest request, CancellationToken cancel)
    {
        if (request.Problem() is { } problem) throw new InvalidOperationException(problem);
        if (Unavailable(model) is { } why) throw new InvalidOperationException(why);

        return await model.DecideAsync(request, Config(model), cancel).ConfigureAwait(false);
    }

    private string NotChosen() => settings.Model == DecisionSettings.Off || models.Count == 0
        ? "No decision model is in use."
        : "No decision model is chosen. Choose one in Settings.";

    internal static string Megabytes(long bytes) => $"{bytes / 1_000_000.0:0} MB";

    private static T? Quietly<T>(Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }
}
