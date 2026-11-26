namespace Flyback.Engine.Language;

/// <summary>One module a pack offers: its type id, its name, its sockets, its settings and its card.</summary>
/// <param name="TypeId">The pack's id and the module's name, as a patch places it: <c>ada.echoes.tape</c>.</param>
/// <param name="Sockets">The parameters a wire can feed, in the order written.</param>
/// <param name="Choices">The parameters written with choices, which are settings rather than sockets.</param>
/// <param name="Outputs">How many things its body ends with, each an output.</param>
/// <param name="Line">Where its <c>module</c> line is, counted from one.</param>
public sealed record PackModule(
    string TypeId,
    string Name,
    IReadOnlyList<PackSocket> Sockets,
    IReadOnlyList<PackChoice> Choices,
    int Outputs,
    ModuleCard Card,
    int Line);
