using Flyback.Core.Compile;

namespace Flyback.Engine.Compile;

/// <summary>
/// A patch's shaders in the Metal Shading Language, one library holding the four
/// functions a frame needs, plus what the caller must bind to them.
/// </summary>
/// <param name="Library">The source of the library, compiled with fast math off.</param>
/// <param name="ConstantCount">How many floats <c>uK</c> reads, in <see cref="GlslEmitter.Constants"/>'s order.</param>
/// <param name="UsesFeedback">Whether the fragment reads the previous frame at texture 0.</param>
/// <param name="LiveCount">How many floats <c>uLive</c> reads, bound every frame.</param>
/// <param name="PictureCount">How many pictures to bind, from texture 1 up, with their aspects.</param>
/// <param name="PlaneTargets">Extra color attachments from 1 up, read back next frame above the pictures.</param>
/// <param name="TableCount">How many float textures to bind above the planes, with their lengths and rates.</param>
public sealed record MetalShaderSource(
    string Library,
    int ConstantCount,
    bool UsesFeedback,
    int OpCount,
    int LiveCount,
    int PictureCount,
    int PlaneTargets,
    int TableCount);
