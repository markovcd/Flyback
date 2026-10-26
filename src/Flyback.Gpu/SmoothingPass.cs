using Flyback.Engine.Compile;
using static Flyback.Gpu.GlConstants;

namespace Flyback.Gpu;

/// <summary>
/// Smooths a finished frame's edges into a target of its own, which the window
/// and a recording then read in place of the history (ADR-0215).
/// </summary>
internal sealed class SmoothingPass
{
    private int program;
    private int sampler = -1;
    private int texelX = -1;
    private int texelY = -1;
    private SurfaceSize size;

    /// <summary>The smoothed frame, eight bits a channel, as the window shows it.</summary>
    public int Texture { get; private set; }

    public int Framebuffer { get; private set; }

    /// <summary>Builds the shader. Null on success.</summary>
    public string? Initialise(IGl gl, GlslDialect dialect)
    {
        var (vertex, fragment) = GlslEmitter.Smoothing(dialect);

        if (GpuFrameRenderer.Link(gl, vertex, fragment, out var error) is not { } linked)
            return $"The smoothing shader would not build. {error}";

        program = linked;
        sampler = gl.GetUniformLocation(program, "uTexture");
        texelX = gl.GetUniformLocation(program, "uTexelX");
        texelY = gl.GetUniformLocation(program, "uTexelY");

        return null;
    }

    /// <summary>Smooths <paramref name="source"/> into <see cref="Texture"/>. Null on success.</summary>
    public string? Draw(IGl gl, int source, SurfaceSize resolution)
    {
        if (program == 0) return "The smoothing shader was never built.";
        if (Resize(gl, resolution) is { } failure) return failure;

        gl.BindFramebuffer(GL_FRAMEBUFFER, Framebuffer);
        gl.Viewport(0, 0, resolution.Width, resolution.Height);
        gl.UseProgram(program);

        gl.ActiveTexture(GL_TEXTURE0);
        gl.BindTexture(GL_TEXTURE_2D, source);

        if (sampler >= 0) gl.Uniform1i(sampler, 0);
        if (texelX >= 0) gl.Uniform1f(texelX, 1f / resolution.Width);
        if (texelY >= 0) gl.Uniform1f(texelY, 1f / resolution.Height);

        gl.DrawArrays(GL_TRIANGLE_STRIP, 0, 4);

        return null;
    }

    private string? Resize(IGl gl, SurfaceSize resolution)
    {
        if (size == resolution && Framebuffer != 0) return null;

        Release(gl);
        size = resolution;

        Texture = gl.GenTexture();
        gl.BindTexture(GL_TEXTURE_2D, Texture);
        gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, resolution.Width, resolution.Height, 0, GL_RGBA, GL_UNSIGNED_BYTE, IntPtr.Zero);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

        Framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(GL_FRAMEBUFFER, Framebuffer);
        gl.FramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, Texture, 0);

        if (gl.CheckFramebufferStatus(GL_FRAMEBUFFER) == GL_FRAMEBUFFER_COMPLETE) return null;

        Release(gl);
        return "This GPU will not render the smoothed frame to an offscreen buffer.";
    }

    /// <summary>The target alone, which a new size makes again.</summary>
    public void Release(IGl gl)
    {
        if (Framebuffer != 0) gl.DeleteFramebuffer(Framebuffer);
        if (Texture != 0) gl.DeleteTexture(Texture);

        Framebuffer = 0;
        Texture = 0;
        size = default;
    }

    /// <summary>Hands everything back; after a lost context, only forgets the names.</summary>
    public void Dispose(IGl? gl)
    {
        if (gl is not null)
        {
            Release(gl);
            if (program != 0) gl.DeleteProgram(program);
        }

        program = 0;
        Framebuffer = 0;
        Texture = 0;
        size = default;
    }
}
