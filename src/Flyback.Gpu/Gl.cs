using System.Runtime.InteropServices;
using System.Text;
using Flyback.Engine.Compile;

namespace Flyback.Gpu;

/// <summary>
/// The OpenGL entry points the renderer calls, found by name through whichever
/// context is current. The same table serves Avalonia's context in the preview and
/// a headless one in flyback-cli.
/// </summary>
/// <remarks>
/// Entry points a context may lack are null and have an <c>Is…Available</c> or a
/// nullable caller; <see cref="Missing"/> names any of the rest that were not found.
/// </remarks>
internal sealed unsafe class Gl : IGl
{
    private readonly Func<string, IntPtr> find;

    private readonly delegate* unmanaged<int> getError;
    private readonly delegate* unmanaged<int, IntPtr> getString;
    private readonly delegate* unmanaged<int, int*, void> getIntegerv;
    private readonly delegate* unmanaged<int, int, IntPtr> getStringi;
    private readonly delegate* unmanaged<void> finish;
    private readonly delegate* unmanaged<int, void> disable;
    private readonly delegate* unmanaged<int, int, int, int, void> viewport;
    private readonly delegate* unmanaged<float, float, float, float, void> clearColor;
    private readonly delegate* unmanaged<int, void> clear;

    private readonly delegate* unmanaged<int, int*, void> genTextures;
    private readonly delegate* unmanaged<int, int*, void> deleteTextures;
    private readonly delegate* unmanaged<int, int, void> bindTexture;
    private readonly delegate* unmanaged<int, int, int, int, int, int, int, int, IntPtr, void> texImage2D;
    private readonly delegate* unmanaged<int, int, int, void> texParameteri;
    private readonly delegate* unmanaged<int, void> activeTexture;

    private readonly delegate* unmanaged<int, int*, void> genFramebuffers;
    private readonly delegate* unmanaged<int, int*, void> deleteFramebuffers;
    private readonly delegate* unmanaged<int, int, void> bindFramebuffer;
    private readonly delegate* unmanaged<int, int, int, int, int, void> framebufferTexture2D;
    private readonly delegate* unmanaged<int, int> checkFramebufferStatus;
    private readonly delegate* unmanaged<int, int, int, int, int, int, int, int, int, int, void> blitFramebuffer;
    private readonly delegate* unmanaged<int, void> readBuffer;
    private readonly delegate* unmanaged<int, IntPtr, void> drawBuffers;
    private readonly delegate* unmanaged<int, int, int, int, int, int, IntPtr, void> readPixels;

    private readonly delegate* unmanaged<int, int*, void> genBuffers;
    private readonly delegate* unmanaged<int, int*, void> deleteBuffers;
    private readonly delegate* unmanaged<int, int, void> bindBuffer;
    private readonly delegate* unmanaged<int, IntPtr, IntPtr, int, void> bufferData;
    private readonly delegate* unmanaged<int, IntPtr, IntPtr, int, IntPtr> mapBufferRange;
    private readonly delegate* unmanaged<int, byte> unmapBuffer;

    private readonly delegate* unmanaged<int, int*, void> genVertexArrays;
    private readonly delegate* unmanaged<int, void> bindVertexArray;
    private readonly delegate* unmanaged<int, int*, void> deleteVertexArrays;

    private readonly delegate* unmanaged<int, int> createShader;
    private readonly delegate* unmanaged<int, int, byte**, int*, void> shaderSource;
    private readonly delegate* unmanaged<int, void> compileShader;
    private readonly delegate* unmanaged<int, void> deleteShader;
    private readonly delegate* unmanaged<int, int, int*, void> getShaderiv;
    private readonly delegate* unmanaged<int, int, int*, byte*, void> getShaderInfoLog;

    private readonly delegate* unmanaged<int> createProgram;
    private readonly delegate* unmanaged<int, int, void> attachShader;
    private readonly delegate* unmanaged<int, void> linkProgram;
    private readonly delegate* unmanaged<int, void> deleteProgram;
    private readonly delegate* unmanaged<int, int, int*, void> getProgramiv;
    private readonly delegate* unmanaged<int, int, int*, byte*, void> getProgramInfoLog;
    private readonly delegate* unmanaged<int, void> useProgram;
    private readonly delegate* unmanaged<int, byte*, int> getUniformLocation;
    private readonly delegate* unmanaged<int, float, void> uniform1f;
    private readonly delegate* unmanaged<int, int, void> uniform1i;
    private readonly delegate* unmanaged<int, int, byte*, void> bindFragDataLocation;
    private readonly delegate* unmanaged<uint, void> maxShaderCompilerThreads;

    private readonly delegate* unmanaged<int, int, int, void> drawArrays;

    private readonly List<string> missing = [];

    /// <param name="getProcAddress">The context's own lookup, which returns zero for a name it lacks.</param>
    public Gl(Func<string, IntPtr> getProcAddress)
    {
        find = getProcAddress;

        getError = (delegate* unmanaged<int>)Required("glGetError");
        getString = (delegate* unmanaged<int, IntPtr>)Required("glGetString");
        getIntegerv = (delegate* unmanaged<int, int*, void>)Required("glGetIntegerv");
        getStringi = (delegate* unmanaged<int, int, IntPtr>)Find("glGetStringi");
        finish = (delegate* unmanaged<void>)Required("glFinish");
        disable = (delegate* unmanaged<int, void>)Required("glDisable");
        viewport = (delegate* unmanaged<int, int, int, int, void>)Required("glViewport");
        clearColor = (delegate* unmanaged<float, float, float, float, void>)Required("glClearColor");
        clear = (delegate* unmanaged<int, void>)Required("glClear");

        genTextures = (delegate* unmanaged<int, int*, void>)Required("glGenTextures");
        deleteTextures = (delegate* unmanaged<int, int*, void>)Required("glDeleteTextures");
        bindTexture = (delegate* unmanaged<int, int, void>)Required("glBindTexture");
        texImage2D = (delegate* unmanaged<int, int, int, int, int, int, int, int, IntPtr, void>)Required("glTexImage2D");
        texParameteri = (delegate* unmanaged<int, int, int, void>)Required("glTexParameteri");
        activeTexture = (delegate* unmanaged<int, void>)Required("glActiveTexture");

        genFramebuffers = (delegate* unmanaged<int, int*, void>)Required("glGenFramebuffers");
        deleteFramebuffers = (delegate* unmanaged<int, int*, void>)Required("glDeleteFramebuffers");
        bindFramebuffer = (delegate* unmanaged<int, int, void>)Required("glBindFramebuffer");
        framebufferTexture2D = (delegate* unmanaged<int, int, int, int, int, void>)Required("glFramebufferTexture2D");
        checkFramebufferStatus = (delegate* unmanaged<int, int>)Required("glCheckFramebufferStatus");
        blitFramebuffer = (delegate* unmanaged<int, int, int, int, int, int, int, int, int, int, void>)Find("glBlitFramebuffer");
        readBuffer = (delegate* unmanaged<int, void>)Find("glReadBuffer");
        drawBuffers = (delegate* unmanaged<int, IntPtr, void>)Find("glDrawBuffers");
        readPixels = (delegate* unmanaged<int, int, int, int, int, int, IntPtr, void>)Find("glReadPixels");

        genBuffers = (delegate* unmanaged<int, int*, void>)Required("glGenBuffers");
        deleteBuffers = (delegate* unmanaged<int, int*, void>)Required("glDeleteBuffers");
        bindBuffer = (delegate* unmanaged<int, int, void>)Required("glBindBuffer");
        bufferData = (delegate* unmanaged<int, IntPtr, IntPtr, int, void>)Required("glBufferData");
        mapBufferRange = (delegate* unmanaged<int, IntPtr, IntPtr, int, IntPtr>)Find("glMapBufferRange");
        unmapBuffer = (delegate* unmanaged<int, byte>)Find("glUnmapBuffer");

        genVertexArrays = (delegate* unmanaged<int, int*, void>)Find("glGenVertexArrays", "glGenVertexArraysOES");
        bindVertexArray = (delegate* unmanaged<int, void>)Find("glBindVertexArray", "glBindVertexArrayOES");
        deleteVertexArrays = (delegate* unmanaged<int, int*, void>)Find("glDeleteVertexArrays", "glDeleteVertexArraysOES");

        createShader = (delegate* unmanaged<int, int>)Required("glCreateShader");
        shaderSource = (delegate* unmanaged<int, int, byte**, int*, void>)Required("glShaderSource");
        compileShader = (delegate* unmanaged<int, void>)Required("glCompileShader");
        deleteShader = (delegate* unmanaged<int, void>)Required("glDeleteShader");
        getShaderiv = (delegate* unmanaged<int, int, int*, void>)Required("glGetShaderiv");
        getShaderInfoLog = (delegate* unmanaged<int, int, int*, byte*, void>)Required("glGetShaderInfoLog");

        createProgram = (delegate* unmanaged<int>)Required("glCreateProgram");
        attachShader = (delegate* unmanaged<int, int, void>)Required("glAttachShader");
        linkProgram = (delegate* unmanaged<int, void>)Required("glLinkProgram");
        deleteProgram = (delegate* unmanaged<int, void>)Required("glDeleteProgram");
        getProgramiv = (delegate* unmanaged<int, int, int*, void>)Required("glGetProgramiv");
        getProgramInfoLog = (delegate* unmanaged<int, int, int*, byte*, void>)Required("glGetProgramInfoLog");
        useProgram = (delegate* unmanaged<int, void>)Required("glUseProgram");
        getUniformLocation = (delegate* unmanaged<int, byte*, int>)Required("glGetUniformLocation");
        uniform1f = (delegate* unmanaged<int, float, void>)Required("glUniform1f");
        uniform1i = (delegate* unmanaged<int, int, void>)Required("glUniform1i");
        bindFragDataLocation = (delegate* unmanaged<int, int, byte*, void>)Find("glBindFragDataLocation");
        maxShaderCompilerThreads = (delegate* unmanaged<uint, void>)Find(
            "glMaxShaderCompilerThreadsKHR", "glMaxShaderCompilerThreadsARB");

        drawArrays = (delegate* unmanaged<int, int, int, void>)Required("glDrawArrays");
    }

    /// <summary>The entry points every context has and this one did not hand over; empty when it is whole.</summary>
    public IReadOnlyList<string> Missing => missing;

    public IntPtr GetProcAddress(string name) => find(name);

    private IntPtr Find(params ReadOnlySpan<string> names)
    {
        foreach (var name in names)
            if (find(name) is var address && address != IntPtr.Zero)
                return address;

        return IntPtr.Zero;
    }

    private IntPtr Required(string name)
    {
        var address = find(name);
        if (address == IntPtr.Zero) missing.Add(name);

        return address;
    }

    public bool IsBlitFramebufferAvailable => blitFramebuffer != null;
    public bool IsReadBufferAvailable => readBuffer != null;
    public bool IsReadPixelsAvailable => readPixels != null;
    public bool IsMapBufferAvailable => mapBufferRange != null && unmapBuffer != null;
    public bool IsDrawBuffersAvailable => drawBuffers != null;
    public bool IsBindFragDataLocationAvailable => bindFragDataLocation != null;
    public bool IsVertexArrayAvailable => genVertexArrays != null && bindVertexArray != null && deleteVertexArrays != null;
    public bool IsMaxShaderCompilerThreadsAvailable => maxShaderCompilerThreads != null;

    public int GetError() => getError();

    public string? GetString(int name) => Marshal.PtrToStringAnsi(getString(name));

    public int GetInteger(int name)
    {
        int value;
        getIntegerv(name, &value);
        return value;
    }

    /// <summary>Whether the context names <paramref name="extension"/> among its own.</summary>
    public bool Supports(string extension)
    {
        if (getStringi == null) return false;

        var count = GetInteger(GlConstants.GL_NUM_EXTENSIONS);

        for (var i = 0; i < count; i++)
            if (Marshal.PtrToStringAnsi(getStringi(GlConstants.GL_EXTENSIONS, i)) == extension)
                return true;

        return false;
    }

    public void Finish() => finish();
    public void Disable(int capability) => disable(capability);
    public void Viewport(int x, int y, int width, int height) => viewport(x, y, width, height);
    public void ClearColor(float r, float g, float b, float a) => clearColor(r, g, b, a);
    public void Clear(int mask) => clear(mask);

    public int GenTexture()
    {
        int name;
        genTextures(1, &name);
        return name;
    }

    public void DeleteTexture(int name) => deleteTextures(1, &name);
    public void BindTexture(int target, int name) => bindTexture(target, name);

    public void TexImage2D(
        int target, int level, int internalFormat, int width, int height, int border, int format, int type, IntPtr pixels) =>
        texImage2D(target, level, internalFormat, width, height, border, format, type, pixels);

    public void TexParameteri(int target, int name, int value) => texParameteri(target, name, value);
    public void ActiveTexture(int unit) => activeTexture(unit);

    public int GenFramebuffer()
    {
        int name;
        genFramebuffers(1, &name);
        return name;
    }

    public void DeleteFramebuffer(int name) => deleteFramebuffers(1, &name);
    public void BindFramebuffer(int target, int name) => bindFramebuffer(target, name);

    public void FramebufferTexture2D(int target, int attachment, int textureTarget, int texture, int level) =>
        framebufferTexture2D(target, attachment, textureTarget, texture, level);

    public int CheckFramebufferStatus(int target) => checkFramebufferStatus(target);

    public void BlitFramebuffer(
        int srcX0, int srcY0, int srcX1, int srcY1, int dstX0, int dstY0, int dstX1, int dstY1, int mask, int filter) =>
        blitFramebuffer(srcX0, srcY0, srcX1, srcY1, dstX0, dstY0, dstX1, dstY1, mask, filter);

    public void ReadBuffer(int source) => readBuffer(source);
    public void DrawBuffers(int count, IntPtr buffers) => drawBuffers(count, buffers);

    public void ReadPixels(int x, int y, int width, int height, int format, int type, IntPtr pixels) =>
        readPixels(x, y, width, height, format, type, pixels);

    public int GenBuffer()
    {
        int name;
        genBuffers(1, &name);
        return name;
    }

    public void DeleteBuffer(int name) => deleteBuffers(1, &name);
    public void BindBuffer(int target, int name) => bindBuffer(target, name);
    public void BufferData(int target, IntPtr size, IntPtr data, int usage) => bufferData(target, size, data, usage);

    public IntPtr MapBufferRange(int target, IntPtr offset, IntPtr length, int access) =>
        mapBufferRange(target, offset, length, access);

    /// <summary>A <c>GLboolean</c> is one byte, and false means the buffer's contents were lost while mapped.</summary>
    public bool UnmapBuffer(int target) => unmapBuffer(target) != 0;

    public int GenVertexArray()
    {
        int name;
        genVertexArrays(1, &name);
        return name;
    }

    public void BindVertexArray(int name) => bindVertexArray(name);
    public void DeleteVertexArray(int name) => deleteVertexArrays(1, &name);

    public int CreateShader(int type) => createShader(type);

    public void ShaderSource(int shader, string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);

        fixed (byte* text = bytes)
        {
            var length = bytes.Length;
            shaderSource(shader, 1, &text, &length);
        }
    }

    public void CompileShader(int shader) => compileShader(shader);
    public void DeleteShader(int shader) => deleteShader(shader);

    public int GetShader(int shader, int name)
    {
        int value;
        getShaderiv(shader, name, &value);
        return value;
    }

    public string ShaderLog(int shader)
    {
        var log = new byte[8192];
        int length;

        fixed (byte* at = log) getShaderInfoLog(shader, log.Length, &length, at);

        return Encoding.UTF8.GetString(log, 0, length);
    }

    public int CreateProgram() => createProgram();
    public void AttachShader(int program, int shader) => attachShader(program, shader);
    public void LinkProgram(int program) => linkProgram(program);
    public void DeleteProgram(int program) => deleteProgram(program);

    public int GetProgram(int program, int name)
    {
        int value;
        getProgramiv(program, name, &value);
        return value;
    }

    public string ProgramLog(int program)
    {
        var log = new byte[8192];
        int length;

        fixed (byte* at = log) getProgramInfoLog(program, log.Length, &length, at);

        return Encoding.UTF8.GetString(log, 0, length);
    }

    public void UseProgram(int program) => useProgram(program);

    public int GetUniformLocation(int program, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name + '\0');

        fixed (byte* text = bytes) return getUniformLocation(program, text);
    }

    public void Uniform1f(int location, float value) => uniform1f(location, value);
    public void Uniform1i(int location, int value) => uniform1i(location, value);

    public void BindFragDataLocation(int program, int color, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name + '\0');

        fixed (byte* text = bytes) bindFragDataLocation(program, color, text);
    }

    public void MaxShaderCompilerThreads(uint count) => maxShaderCompilerThreads(count);

    public void DrawArrays(int mode, int first, int count) => drawArrays(mode, first, count);
}
