namespace Flyback.Engine.Language;

/// <summary>One module a pack offers: its type id, its name, its sockets and its card.</summary>
/// <param name="TypeId">The pack's id and the module's name, as a patch places it: <c>ada.echoes.tape</c>.</param>
/// <param name="Inputs">Its parameters' names, which are its sockets.</param>
/// <param name="Defaults">What each socket holds with no wire, from the parameter's default.</param>
/// <param name="Outputs">How many things its body ends with, each an output.</param>
/// <param name="Line">Where its <c>module</c> line is, counted from one.</param>
public sealed record PackModule(
    string TypeId,
    string Name,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<float> Defaults,
    int Outputs,
    ModuleCard Card,
    int Line);
