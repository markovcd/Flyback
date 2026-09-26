namespace Flyback.Core.Compile;

/// <summary>The four shaders a frame needs, plus what the caller must upload to them.</summary>
/// <param name="ConstantCount">
/// Length of the <c>uK</c> array. Every <see cref="OpCode.Const"/> is a uniform
/// rather than a literal — see <see cref="GlslEmitter"/>.
/// </param>
/// <param name="UsesFeedback">Whether the fragment shader reads <c>uPrevious</c>.</param>
/// <param name="LiveCount">Length of the <c>uLive</c> array, uploaded every frame.</param>
/// <param name="PictureCount">How many textures to bind, from <see cref="CompiledPatch.Pictures"/> in order.</param>
/// <param name="PlaneTargets">
/// Extra render targets, one per four planes (<see cref="OpCode.PlaneRead"/>):
/// written as <c>outPlane0</c> up at locations 1 up, read back next frame as
/// <c>uPlane0</c> up.
/// </param>
public sealed record ShaderSource(
    string PatchVertex,
    string PatchFragment,
    string BlitVertex,
    string BlitFragment,
    int ConstantCount,
    bool UsesFeedback,
    int OpCount,
    int LiveCount = 0,
    int PictureCount = 0,
    int PlaneTargets = 0);