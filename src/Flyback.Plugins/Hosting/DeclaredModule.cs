namespace Flyback.Plugins.Hosting;

/// <summary>A module a plugin declares with <see cref="FlybackModuleAttribute"/>.</summary>
internal sealed record DeclaredModule(string TypeId, string Name);