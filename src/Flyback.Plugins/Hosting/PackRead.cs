using Flyback.Core.Graph;

namespace Flyback.Plugins.Hosting;

/// <summary>A pack's text that reads cleanly: its id, its version and the modules it offers.</summary>
internal sealed record PackRead(string Id, string Version, IReadOnlyList<NodeDef> Modules);
