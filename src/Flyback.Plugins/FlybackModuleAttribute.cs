namespace Flyback.Plugins;

/// <summary>
/// Declares a module the plugin registers, so the install dialog and the shared plugins
/// site can list it without running the plugin.
/// </summary>
/// <remarks>
/// One per module, on the assembly, with the type id and name its <c>NodeDef</c> has:
/// <code>[assembly: FlybackModule(CircleModule.TypeId, "Circle")]</code>
/// Flyback refuses a plugin compiled against this contract or later that registers a
/// module it does not declare here, or declares under another name. A module declared
/// and not registered is allowed, for one a plugin leaves out on some systems.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class FlybackModuleAttribute(string id, string name) : Attribute
{
    /// <summary>The module's type id, as its <c>NodeDef.TypeId</c>.</summary>
    public string Id { get; } = id;

    public string Name { get; } = name;
}
