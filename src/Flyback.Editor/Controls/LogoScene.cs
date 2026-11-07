using Flyback.Core.Graph;
using Flyback.Engine.Language;

namespace Flyback.Editor.Controls;

/// <summary>
/// The mark as a patch rather than a drawing: an old set in a dark room, its tube
/// drawing the logo a bar at a time, and an IDM loop playing on it.
/// </summary>
/// <remarks>
/// Written as text, <c>LogoScene.fbks</c>, which is also what the About window copies,
/// so what is pasted is the scene with its comments. Every module is the engine's own,
/// so it pastes into any build. The About window plays the same patch with its camera
/// on the tube, silent: a sound nobody asked for is a fault in a dialog.
/// </remarks>
internal static class LogoScene
{
    /// <summary>The Transform every position in the scene is read through.</summary>
    private const string Camera = "view";

    /// <summary>The scene as it is written, comments and all.</summary>
    public static string Text { get; } = Read();

    /// <summary>The whole scene, the room and the music.</summary>
    /// <remarks>A text that does not build throws with every complaint, since that is a bug here.</remarks>
    public static Patch Patch()
    {
        var load = PatchLanguage.Build(Text, NodeCatalog.BuiltIn);

        return load.Ok ? load.Patch : throw new InvalidOperationException($"The logo scene does not build:{Environment.NewLine}{load.Report}");
    }

    /// <summary>
    /// The scene with its camera on the tube: on a square frame the glass fills it and
    /// the bezel shows at the corners.
    /// </summary>
    public static Patch Framed()
    {
        var patch = Patch();
        var camera = patch.Nodes.Single(node => node.Name == Camera);
        var sockets = NodeCatalog.BuiltIn.Require(camera.TypeId).Inputs;

        void Turn(string socket, float value) =>
            camera.InputValues[sockets.ToList().FindIndex(port => port.Name == socket)] = value;

        Turn("zoom", 0.4f);
        Turn("dx", 0.2f);
        Turn("dy", -0.07f);

        return patch;
    }

    private static string Read()
    {
        using var stream = typeof(LogoScene).Assembly.GetManifestResourceStream("logo-scene.fbks")
            ?? throw new InvalidOperationException("The logo scene is not embedded.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
