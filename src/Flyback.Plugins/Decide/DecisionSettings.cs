using System.Text.Encodings.Web;
using System.Text.Json;
using Flyback.Core;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Decide;

/// <summary>Which decision model the editor and the command line ask, and what each one was set to. No key is in here (ADR-0034).</summary>
internal sealed class DecisionSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Where these settings are kept in <see cref="SettingsFile"/>.</summary>
    public const string Section = "decisions";

    /// <summary>What <see cref="Model"/> holds when somebody turned decisions off.</summary>
    public const string Off = "none";

    /// <summary>
    /// Which model, by <see cref="IDecisionModel.Id"/>; <see cref="Off"/> for none. Null until
    /// somebody chooses, which means the first installed model that sends nothing anywhere.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>What each model was last set to, filed under its id.</summary>
    public Dictionary<string, Dictionary<string, string>> Choices { get; set; } = new(StringComparer.Ordinal);

    /// <summary>What a <see cref="DecisionUse"/> lays over a model's settings, filed under the use, then the model.</summary>
    public Dictionary<string, Dictionary<string, Dictionary<string, string>>> Uses { get; set; } = new(StringComparer.Ordinal);

    /// <summary>What is set for one model.</summary>
    public SettingValues Of(string model) =>
        Choices.TryGetValue(model, out var held) ? new SettingValues(held) : SettingValues.None;

    /// <summary>What is set for one model when it is asked for <paramref name="use"/>: its own settings, with the use's laid over them.</summary>
    public SettingValues Of(string model, string? use) => Laid(Of(model), Over(model, use));

    /// <summary><paramref name="under"/> with every pair of <paramref name="over"/> laid on it.</summary>
    public static SettingValues Laid(SettingValues under, SettingValues over)
    {
        var values = under;

        foreach (var (key, value) in over.All) values = values.With(key, value);

        return values;
    }

    /// <summary>Only what <paramref name="use"/> lays over the model's settings.</summary>
    public SettingValues Over(string model, string? use) =>
        use is not null && Uses.TryGetValue(use, out var models) && models.TryGetValue(model, out var held)
            ? new SettingValues(held)
            : SettingValues.None;

    /// <summary>Takes one model's answers, leaving every other model's alone.</summary>
    public void Remember(string model, SettingValues values) =>
        Choices[model] = new Dictionary<string, string>(values.All, StringComparer.Ordinal);

    /// <summary>Takes what <paramref name="use"/> lays over one model's settings; nothing laid over removes the use's entry.</summary>
    public void Remember(string model, string use, SettingValues over)
    {
        if (!Uses.TryGetValue(use, out var models)) Uses[use] = models = new(StringComparer.Ordinal);

        if (over.All.Count > 0) models[model] = new Dictionary<string, string>(over.All, StringComparer.Ordinal);
        else models.Remove(model);

        if (models.Count == 0) Uses.Remove(use);
    }

    /// <summary>Never throws: a settings file is not worth a failure to start.</summary>
    public static DecisionSettings Load(string? path = null)
    {
        try
        {
            return SettingsFile.Read(path ?? SettingsFile.Path, Section) is { } json
                ? JsonSerializer.Deserialize<DecisionSettings>(json, Options) ?? new()
                : new DecisionSettings();
        }
        catch
        {
            return new DecisionSettings();
        }
    }

    /// <summary>Writes the choices out. Throws if it cannot, so the caller can say so.</summary>
    public void Save(string? path = null) =>
        SettingsFile.Write(path ?? SettingsFile.Path, Section, JsonSerializer.Serialize(this, Options));
}
