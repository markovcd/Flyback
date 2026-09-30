using System.Text;

namespace Flyback.Specs.Support;

/// <summary>A patch file of one module from a plugin nothing here has, stamped as needing it, as a submitted plugin's preset is.</summary>
public static class PluginPatch
{
    public static byte[] Needing(string plugin)
    {
        var id = "example." + plugin.ToLowerInvariant();

        return Encoding.UTF8.GetBytes($$"""
            {
              "Requires": [ { "Id": "{{id}}", "Name": "{{plugin}}" } ],
              "Nodes": [ { "Id": "8f9d1d3e-0000-4000-8000-000000000012", "TypeId": "{{id}}.glow" } ],
              "Connections": []
            }
            """);
    }
}
