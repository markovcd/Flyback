using System.Runtime.InteropServices.JavaScript;
using System.Text.Json.Nodes;
using Flyback.App;
using Flyback.App.Canvas;
using Flyback.App.Controls;
using Flyback.App.Gallery;
using Microsoft.Extensions.DependencyInjection;

namespace Flyback.WebEditor;

/// <summary>
/// What a script driving the page can ask of the editor without looking at it:
/// <c>window.flyback</c> in <c>main.js</c>.
/// </summary>
internal static partial class PageExports
{
    internal static IServiceProvider? Provider { get; set; }

    private static T Get<T>() where T : notnull =>
        (Provider ?? throw new InvalidOperationException("The editor has not started.")).GetRequiredService<T>();

    /// <summary>The patch on the canvas, the preview drawing it, and the last thing the editor said, as JSON.</summary>
    [JSExport]
    public static string State()
    {
        if (Provider is null) return new JsonObject { ["started"] = false }.ToJsonString();

        var patch = Get<CanvasHistory>().Patch;
        var preview = Get<PreviewHost>();
        var said = Get<ReportLine>().History;

        return new JsonObject
        {
            ["started"] = true,
            ["preset"] = Get<PresetSlot>().Showing?.Name,
            ["modules"] = patch.Nodes.Count,
            ["wires"] = patch.Connections.Count,
            ["backend"] = preview.Backend.ToString(),
            ["renderer"] = preview.Renderer,
            ["framesPerSecond"] = Math.Round(preview.FramesPerSecond, 1),
            ["frameMilliseconds"] = Math.Round(preview.FrameMilliseconds, 2),
            ["time"] = Math.Round(preview.Time, 2),
            ["said"] = said.Count > 0 ? said[^1] : null,
        }.ToJsonString();
    }

    /// <summary>Opens the preset called <paramref name="name"/>, or the first on the list where none is.</summary>
    [JSExport]
    public static void Preset(string name) => Get<PresetSlot>().StartOn(name);

    /// <summary>
    /// Opens a shared preset's file, fetched from the preset site, under <paramref name="name"/>.
    /// Null once open, or what the editor said instead.
    /// </summary>
    [JSExport]
    public static async Task<string?> Shared(string name, string fileName, byte[] bytes)
    {
        if (await Get<PresetSlot>().OpenSharedAsync(name, fileName, bytes)) return null;

        var said = Get<ReportLine>().History;

        return said.Count > 0 ? said[^1] : "Not opened.";
    }

    /// <summary>The open patch as text in the language.</summary>
    [JSExport]
    public static string Text() => Get<Document>().AsText();

    /// <summary>Applies <paramref name="text"/> as the text view's Apply does; null once applied, or what is wrong with it.</summary>
    [JSExport]
    public static string? Apply(string text) => Get<Document>().Apply(text);
}
