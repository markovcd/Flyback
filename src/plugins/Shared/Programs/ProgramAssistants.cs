using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Programs;

/// <summary>What an assistant that is a program signed in by its person has in common.</summary>
internal static class ProgramAssistants
{
    /// <summary>
    /// The schema's questions less the two that have no answer: there is no address
    /// to set, and no model takes a sound.
    /// </summary>
    public static IReadOnlyList<SettingField> Form(AssistantSchema schema, SettingValues values) =>
        [.. schema.Form(values).Where(field => field.Key is not (AssistantSchema.EndpointKey or AssistantSchema.HearingKey))];

    /// <summary>A conversation over <paramref name="program"/>, which every question fails to reach where it is not installed.</summary>
    public static ProgramSession Start(
        string name, PatchWorkbench workbench, AssistantSchema schema, AssistantConfig config, IProgram? program) =>
        new(workbench, schema.Read(config.Values), program ?? new Missing(name));

    /// <summary>The conversation <paramref name="saved"/> held, or null where it is not one.</summary>
    public static IPatchSession? Resume(
        string name, PatchWorkbench workbench, AssistantSchema schema, AssistantConfig config, IProgram? program, string saved)
    {
        var session = Start(name, workbench, schema, config, program);

        if (session.Take(saved)) return session;

        session.Dispose();
        return null;
    }

    /// <summary>What a session is given where the program is not installed: every question is a failure that says so.</summary>
    private sealed class Missing(string name) : IProgram
    {
        public Task<ProgramAnswer> Ask(ProgramQuestion question, CancellationToken cancel) =>
            throw new ProgramFailure($"{name} is not installed.");
    }
}
