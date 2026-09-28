namespace Flyback.Gpu;

/// <summary>The OpenGL enumerants the renderer names, spelled as the specification spells them.</summary>
internal static class GlConstants
{
    public const int GL_NO_ERROR = 0;
    public const int GL_INVALID_OPERATION = 0x0502;

    public const int GL_TRIANGLE_STRIP = 0x0005;
    public const int GL_COLOR_BUFFER_BIT = 0x4000;

    public const int GL_CULL_FACE = 0x0B44;
    public const int GL_DEPTH_TEST = 0x0B71;
    public const int GL_BLEND = 0x0BE2;
    public const int GL_SCISSOR_TEST = 0x0C11;

    public const int GL_RENDERER = 0x1F01;
    public const int GL_VERSION = 0x1F02;
    public const int GL_EXTENSIONS = 0x1F03;
    public const int GL_NUM_EXTENSIONS = 0x821D;

    public const int GL_TEXTURE_2D = 0x0DE1;
    public const int GL_UNSIGNED_BYTE = 0x1401;
    public const int GL_FLOAT = 0x1406;
    public const int GL_HALF_FLOAT = 0x140B;
    public const int GL_RGBA = 0x1908;
    public const int GL_RGBA8 = 0x8058;
    public const int GL_RGBA32F = 0x8814;
    public const int GL_RGBA16F = 0x881A;

    public const int GL_NEAREST = 0x2600;
    public const int GL_LINEAR = 0x2601;
    public const int GL_TEXTURE_MAG_FILTER = 0x2800;
    public const int GL_TEXTURE_MIN_FILTER = 0x2801;
    public const int GL_TEXTURE_WRAP_S = 0x2802;
    public const int GL_TEXTURE_WRAP_T = 0x2803;
    public const int GL_CLAMP_TO_EDGE = 0x812F;
    public const int GL_TEXTURE0 = 0x84C0;

    public const int GL_FRAMEBUFFER = 0x8D40;
    public const int GL_READ_FRAMEBUFFER = 0x8CA8;
    public const int GL_DRAW_FRAMEBUFFER = 0x8CA9;
    public const int GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    public const int GL_COLOR_ATTACHMENT0 = 0x8CE0;

    public const int GL_PIXEL_PACK_BUFFER = 0x88EB;
    public const int GL_STREAM_READ = 0x88E1;
    public const int GL_MAP_READ_BIT = 0x0001;

    public const int GL_FRAGMENT_SHADER = 0x8B30;
    public const int GL_VERTEX_SHADER = 0x8B31;
    public const int GL_COMPILE_STATUS = 0x8B81;
    public const int GL_LINK_STATUS = 0x8B82;

    /// <summary><c>KHR_parallel_shader_compile</c>'s question: has the driver finished with this yet.</summary>
    public const int GL_COMPLETION_STATUS = 0x91B1;
}
