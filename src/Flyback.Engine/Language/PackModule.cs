namespace Flyback.Engine.Language;

/// <summary>One module a pack offers: its type id, its name, its parameters and its card.</summary>
/// <param name="TypeId">The pack's id and the module's name, as a patch places it: <c>ada.echoes.tape</c>.</param>
/// <param name="Line">Where its <c>module</c> line is, counted from one.</param>
public sealed record PackModule(string TypeId, string Name, IReadOnlyList<string> Inputs, ModuleCard Card, int Line);
