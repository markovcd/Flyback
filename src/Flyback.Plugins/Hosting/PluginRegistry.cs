using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Midi;
using Flyback.Plugins.Secrets;

namespace Flyback.Plugins.Hosting;

/// <summary>
/// Collects what plugins offer, clashes and all: <see cref="PluginCatalogBuilder"/> refuses an id
/// offered twice once every plugin has had its turn.
/// </summary>
internal sealed class PluginRegistry(List<PluginProblem> problems) : IPluginRegistry
{
    private readonly List<IAudioOutput> audioOutputs = [];
    private readonly List<IPatchAssistant> assistants = [];
    private readonly List<ISecretStore> secretStores = [];
    private readonly List<IMidiInput> midiInputs = [];
    private readonly List<IAudioInput> audioInputs = [];
    private readonly List<IDecisionModel> decisionModels = [];

    /// <summary>Who registered each thing kept above, keyed by the thing itself.</summary>
    private readonly Dictionary<object, PluginInfo> providers = new(ReferenceEqualityComparer.Instance);

    /// <summary>Whoever is registering right now, for blaming in messages.</summary>
    public PluginInfo Source { get; set; } = new("", "");

    /// <summary>Whether whoever is registering right now may register a secret store.</summary>
    public bool Secrets { get; set; } = true;

    public IReadOnlyDictionary<object, PluginInfo> Providers => providers;

    public IReadOnlyList<IAudioOutput> AudioOutputs => audioOutputs;

    public IReadOnlyList<IPatchAssistant> Assistants => assistants;

    public IReadOnlyList<ISecretStore> SecretStores => secretStores;

    public IReadOnlyList<IMidiInput> MidiInputs => midiInputs;

    public IReadOnlyList<IAudioInput> AudioInputs => audioInputs;

    public IReadOnlyList<IDecisionModel> DecisionModels => decisionModels;

    /// <summary>Every module offered, accepted or not, in order.</summary>
    private readonly List<NodeDef> offered = [];

    /// <summary>How far everything had got, to undo a plugin's registration back to.</summary>
    public readonly record struct Checkpoint(
        ModuleCatalog Modules, int Offered, int Presets, int AudioOutputs, int Assistants, int SecretStores, int MidiInputs, int AudioInputs, int DecisionModels, int Problems);

    public Checkpoint Mark() => new(
        Modules, offered.Count, presets.Count, audioOutputs.Count, assistants.Count, secretStores.Count, midiInputs.Count, audioInputs.Count, decisionModels.Count, problems.Count);

    public IEnumerable<NodeDef> OfferedSince(Checkpoint mark) => offered.Skip(mark.Offered);

    /// <summary>Forgets everything registered since <paramref name="mark"/>, and the problems it caused.</summary>
    public void Restore(Checkpoint mark)
    {
        Modules = mark.Modules;
        offered.RemoveRange(mark.Offered, offered.Count - mark.Offered);
        presets.RemoveRange(mark.Presets, presets.Count - mark.Presets);
        Trim(audioOutputs, mark.AudioOutputs);
        Trim(assistants, mark.Assistants);
        Trim(secretStores, mark.SecretStores);
        Trim(midiInputs, mark.MidiInputs);
        Trim(audioInputs, mark.AudioInputs);
        Trim(decisionModels, mark.DecisionModels);
        problems.RemoveRange(mark.Problems, problems.Count - mark.Problems);
    }

    private void Trim<T>(List<T> list, int count) where T : notnull
    {
        foreach (var removed in list.Skip(count)) providers.Remove(removed);

        list.RemoveRange(count, list.Count - count);
    }

    /// <summary>
    /// Built up as plugins register, starting from the engine's own modules.
    /// The catalog itself decides what it will accept; refusals become
    /// problems here so a plugin author sees them next to everything else
    /// that went wrong.
    /// </summary>
    public ModuleCatalog Modules { get; private set; } = NodeCatalog.BuiltIn;

    /// <summary>Starts as the engine's own presets; plugins append to it.</summary>
    private readonly List<PatchPreset> presets = [.. Engine.Graph.Presets.All];

    public IReadOnlyList<PatchPreset> Presets => presets;

    public void AddPresets(IReadOnlyList<PatchPreset> offered)
    {
        foreach (var preset in offered)
        {
            if (presets.Any(p => p.Name == preset.Name))
            {
                problems.Add(new PluginProblem(
                    Source.Id,
                    $"preset '{preset.Name}' is already offered and was ignored."));
                continue;
            }

            // A plugin's preset arrives with its Maths chains folded into
            // Expressions, the same as the engine's.
            presets.Add(Engine.Graph.Presets.Fused(preset));
        }
    }

    public void AddModules(ModuleProvider provider, IReadOnlyList<NodeDef> modules)
    {
        offered.AddRange(modules);

        var added = Modules.With(provider, modules);

        Modules = added.Catalog;

        foreach (var rejection in added.Rejected)
            problems.Add(new PluginProblem(Source.Id, rejection));
    }

    public void AddAudioOutput(IAudioOutput output)
    {
        audioOutputs.Add(output);
        providers[output] = Source;
    }

    public void AddPatchAssistant(IPatchAssistant assistant)
    {
        assistants.Add(assistant);
        providers[assistant] = Source;
    }

    public void AddSecretStore(ISecretStore store)
    {
        if (!Secrets)
        {
            problems.Add(new PluginProblem(
                Source.Id,
                $"secret store '{store.Id}' is refused: only a plugin Flyback ships, or one allowed with `flyback-cli plugin allow --secrets`, may keep keys."));
            return;
        }

        secretStores.Add(store);
        providers[store] = Source;
    }

    public void AddMidiInput(IMidiInput input)
    {
        midiInputs.Add(input);
        providers[input] = Source;
    }

    public void AddAudioInput(IAudioInput input)
    {
        audioInputs.Add(input);
        providers[input] = Source;
    }

    public void AddDecisionModel(IDecisionModel model)
    {
        decisionModels.Add(model);
        providers[model] = Source;
    }
}
