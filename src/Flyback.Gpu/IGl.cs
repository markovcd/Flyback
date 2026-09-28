namespace Flyback.Gpu;

/// <summary>
/// The OpenGL calls <see cref="GpuFrameRenderer"/> makes, so the same renderer draws
/// through a native context or through a browser's WebGL 2.
/// </summary>
internal interface IGl
{
    /// <summary>The entry points every context has and this one did not hand over; empty when it is whole.</summary>
    IReadOnlyList<string> Missing { get; }

    bool IsBlitFramebufferAvailable { get; }
    bool IsReadBufferAvailable { get; }
    bool IsReadPixelsAvailable { get; }
    bool IsMapBufferAvailable { get; }
    bool IsDrawBuffersAvailable { get; }
    bool IsBindFragDataLocationAvailable { get; }
    bool IsVertexArrayAvailable { get; }
    bool IsMaxShaderCompilerThreadsAvailable { get; }

    int GetError();

    /// <summary>Whether the context names <paramref name="extension"/> among its own.</summary>
    bool Supports(string extension);

    void Disable(int capability);
    void Viewport(int x, int y, int width, int height);
    void ClearColor(float r, float g, float b, float a);
    void Clear(int mask);

    int GenTexture();
    void DeleteTexture(int name);
    void BindTexture(int target, int name);

    void TexImage2D(
        int target, int level, int internalFormat, int width, int height, int border, int format, int type, IntPtr pixels);

    void TexParameteri(int target, int name, int value);
    void ActiveTexture(int unit);

    int GenFramebuffer();
    void DeleteFramebuffer(int name);
    void BindFramebuffer(int target, int name);
    void FramebufferTexture2D(int target, int attachment, int textureTarget, int texture, int level);
    int CheckFramebufferStatus(int target);

    void BlitFramebuffer(
        int srcX0, int srcY0, int srcX1, int srcY1, int dstX0, int dstY0, int dstX1, int dstY1, int mask, int filter);

    void ReadBuffer(int source);
    void DrawBuffers(int count, IntPtr buffers);
    void ReadPixels(int x, int y, int width, int height, int format, int type, IntPtr pixels);

    int GenBuffer();
    void DeleteBuffer(int name);
    void BindBuffer(int target, int name);
    void BufferData(int target, IntPtr size, IntPtr data, int usage);
    IntPtr MapBufferRange(int target, IntPtr offset, IntPtr length, int access);

    /// <summary>False means the buffer's contents were lost while mapped.</summary>
    bool UnmapBuffer(int target);

    int GenVertexArray();
    void BindVertexArray(int name);
    void DeleteVertexArray(int name);

    int CreateShader(int type);
    void ShaderSource(int shader, string source);
    void CompileShader(int shader);
    void DeleteShader(int shader);
    int GetShader(int shader, int name);
    string ShaderLog(int shader);

    int CreateProgram();
    void AttachShader(int program, int shader);
    void LinkProgram(int program);
    void DeleteProgram(int program);
    int GetProgram(int program, int name);
    string ProgramLog(int program);
    void UseProgram(int program);

    int GetUniformLocation(int program, string name);
    void Uniform1f(int location, float value);
    void Uniform1i(int location, int value);
    void BindFragDataLocation(int program, int color, string name);
    void MaxShaderCompilerThreads(uint count);

    void DrawArrays(int mode, int first, int count);

    /// <summary>Compiles <paramref name="source"/> into <paramref name="shader"/>, waiting for it. Null on success.</summary>
    string? CompileShaderAndGetError(int shader, string source)
    {
        ShaderSource(shader, source);
        CompileShader(shader);

        return GetShader(shader, GlConstants.GL_COMPILE_STATUS) != 0 ? null : ShaderLog(shader);
    }

    /// <summary>Links <paramref name="program"/>, waiting for it. Null on success.</summary>
    string? LinkProgramAndGetError(int program)
    {
        LinkProgram(program);

        return GetProgram(program, GlConstants.GL_LINK_STATUS) != 0 ? null : ProgramLog(program);
    }
}
