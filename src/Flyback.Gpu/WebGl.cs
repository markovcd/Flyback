using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Flyback.Engine.Compile;

namespace Flyback.Gpu;

/// <summary>
/// The desktop's GL calls made on a page's WebGL 2 context, through the web viewer's <c>gl.js</c>.
/// Every GL name is an integer there as it is here; the script keeps the objects.
/// </summary>
/// <remarks>
/// A pointer argument is an address in the runtime's own memory, which the script
/// reads and writes through the heap view the runtime hands it.
/// </remarks>
[SupportedOSPlatform("browser")]
internal sealed partial class WebGl : IGl
{
    public const string Module = "gl";

    public IReadOnlyList<string> Missing => [];

    public bool IsBlitFramebufferAvailable => true;
    public bool IsReadBufferAvailable => true;
    public bool IsReadPixelsAvailable => true;
    public bool IsMapBufferAvailable => false;
    public bool IsDrawBuffersAvailable => true;
    public bool IsBindFragDataLocationAvailable => false;
    public bool IsVertexArrayAvailable => true;
    public bool IsMaxShaderCompilerThreadsAvailable => false;

    public int GetError() => JsGetError();
    public bool Supports(string extension) => JsSupports(extension);
    public void Disable(int capability) => JsDisable(capability);
    public void Viewport(int x, int y, int width, int height) => JsViewport(x, y, width, height);
    public void ClearColor(float r, float g, float b, float a) => JsClearColor(r, g, b, a);
    public void Clear(int mask) => JsClear(mask);

    public int GenTexture() => JsGenTexture();
    public void DeleteTexture(int name) => JsDeleteTexture(name);
    public void BindTexture(int target, int name) => JsBindTexture(target, name);

    public void TexImage2D(
        int target, int level, int internalFormat, int width, int height, int border, int format, int type, IntPtr pixels) =>
        JsTexImage2D(target, level, internalFormat, width, height, format, type, pixels);

    public void TexParameteri(int target, int name, int value) => JsTexParameteri(target, name, value);
    public void ActiveTexture(int unit) => JsActiveTexture(unit);

    public int GenFramebuffer() => JsGenFramebuffer();
    public void DeleteFramebuffer(int name) => JsDeleteFramebuffer(name);
    public void BindFramebuffer(int target, int name) => JsBindFramebuffer(target, name);

    public void FramebufferTexture2D(int target, int attachment, int textureTarget, int texture, int level) =>
        JsFramebufferTexture2D(target, attachment, textureTarget, texture, level);

    public int CheckFramebufferStatus(int target) => JsCheckFramebufferStatus(target);

    public void BlitFramebuffer(
        int srcX0, int srcY0, int srcX1, int srcY1, int dstX0, int dstY0, int dstX1, int dstY1, int mask, int filter) =>
        JsBlitFramebuffer(srcX0, srcY0, srcX1, srcY1, dstX0, dstY0, dstX1, dstY1, mask, filter);

    public void ReadBuffer(int source) => JsReadBuffer(source);
    public void DrawBuffers(int count, IntPtr buffers) => JsDrawBuffers(count, buffers);

    public void ReadPixels(int x, int y, int width, int height, int format, int type, IntPtr pixels) =>
        JsReadPixels(x, y, width, height, format, type, pixels);

    public int GenBuffer() => JsGenBuffer();
    public void DeleteBuffer(int name) => JsDeleteBuffer(name);
    public void BindBuffer(int target, int name) => JsBindBuffer(target, name);
    public void BufferData(int target, IntPtr size, IntPtr data, int usage) => JsBufferData(target, checked((int)size), data, usage);

    public IntPtr MapBufferRange(int target, IntPtr offset, IntPtr length, int access) =>
        throw new NotSupportedException("WebGL maps no buffers.");

    public bool UnmapBuffer(int target) => throw new NotSupportedException("WebGL maps no buffers.");

    public int GenVertexArray() => JsGenVertexArray();
    public void BindVertexArray(int name) => JsBindVertexArray(name);
    public void DeleteVertexArray(int name) => JsDeleteVertexArray(name);

    public int CreateShader(int type) => JsCreateShader(type);
    public void ShaderSource(int shader, string source) => JsShaderSource(shader, source);
    public void CompileShader(int shader) => JsCompileShader(shader);
    public void DeleteShader(int shader) => JsDeleteShader(shader);
    public int GetShader(int shader, int name) => JsGetShader(shader, name);
    public string ShaderLog(int shader) => JsShaderLog(shader);

    public int CreateProgram() => JsCreateProgram();
    public void AttachShader(int program, int shader) => JsAttachShader(program, shader);
    public void LinkProgram(int program) => JsLinkProgram(program);
    public void DeleteProgram(int program) => JsDeleteProgram(program);
    public int GetProgram(int program, int name) => JsGetProgram(program, name);
    public string ProgramLog(int program) => JsProgramLog(program);
    public void UseProgram(int program) => JsUseProgram(program);

    public int GetUniformLocation(int program, string name) => JsGetUniformLocation(program, name);
    public void Uniform1f(int location, float value) => JsUniform1f(location, value);
    public void Uniform1i(int location, int value) => JsUniform1i(location, value);

    public void BindFragDataLocation(int program, int color, string name) =>
        throw new NotSupportedException("An ES shader names its own outputs.");

    public void MaxShaderCompilerThreads(uint count) =>
        throw new NotSupportedException("WebGL chooses its own compiler threads.");

    public void DrawArrays(int mode, int first, int count) => JsDrawArrays(mode, first, count);

    [JSImport("getError", Module)] private static partial int JsGetError();
    [JSImport("supports", Module)] private static partial bool JsSupports(string extension);
    [JSImport("disable", Module)] private static partial void JsDisable(int capability);
    [JSImport("viewport", Module)] private static partial void JsViewport(int x, int y, int width, int height);
    [JSImport("clearColor", Module)] private static partial void JsClearColor(float r, float g, float b, float a);
    [JSImport("clear", Module)] private static partial void JsClear(int mask);

    [JSImport("genTexture", Module)] private static partial int JsGenTexture();
    [JSImport("deleteTexture", Module)] private static partial void JsDeleteTexture(int name);
    [JSImport("bindTexture", Module)] private static partial void JsBindTexture(int target, int name);

    [JSImport("texImage2D", Module)]
    private static partial void JsTexImage2D(
        int target, int level, int internalFormat, int width, int height, int format, int type, IntPtr pixels);

    [JSImport("texParameteri", Module)] private static partial void JsTexParameteri(int target, int name, int value);
    [JSImport("activeTexture", Module)] private static partial void JsActiveTexture(int unit);

    [JSImport("genFramebuffer", Module)] private static partial int JsGenFramebuffer();
    [JSImport("deleteFramebuffer", Module)] private static partial void JsDeleteFramebuffer(int name);
    [JSImport("bindFramebuffer", Module)] private static partial void JsBindFramebuffer(int target, int name);

    [JSImport("framebufferTexture2D", Module)]
    private static partial void JsFramebufferTexture2D(int target, int attachment, int textureTarget, int texture, int level);

    [JSImport("checkFramebufferStatus", Module)] private static partial int JsCheckFramebufferStatus(int target);

    [JSImport("blitFramebuffer", Module)]
    private static partial void JsBlitFramebuffer(
        int srcX0, int srcY0, int srcX1, int srcY1, int dstX0, int dstY0, int dstX1, int dstY1, int mask, int filter);

    [JSImport("readBuffer", Module)] private static partial void JsReadBuffer(int source);
    [JSImport("drawBuffers", Module)] private static partial void JsDrawBuffers(int count, IntPtr buffers);

    [JSImport("readPixels", Module)]
    private static partial void JsReadPixels(int x, int y, int width, int height, int format, int type, IntPtr pixels);

    [JSImport("genBuffer", Module)] private static partial int JsGenBuffer();
    [JSImport("deleteBuffer", Module)] private static partial void JsDeleteBuffer(int name);
    [JSImport("bindBuffer", Module)] private static partial void JsBindBuffer(int target, int name);
    [JSImport("bufferData", Module)] private static partial void JsBufferData(int target, int size, IntPtr data, int usage);

    [JSImport("genVertexArray", Module)] private static partial int JsGenVertexArray();
    [JSImport("bindVertexArray", Module)] private static partial void JsBindVertexArray(int name);
    [JSImport("deleteVertexArray", Module)] private static partial void JsDeleteVertexArray(int name);

    [JSImport("createShader", Module)] private static partial int JsCreateShader(int type);
    [JSImport("shaderSource", Module)] private static partial void JsShaderSource(int shader, string source);
    [JSImport("compileShader", Module)] private static partial void JsCompileShader(int shader);
    [JSImport("deleteShader", Module)] private static partial void JsDeleteShader(int shader);
    [JSImport("getShader", Module)] private static partial int JsGetShader(int shader, int name);
    [JSImport("shaderLog", Module)] private static partial string JsShaderLog(int shader);

    [JSImport("createProgram", Module)] private static partial int JsCreateProgram();
    [JSImport("attachShader", Module)] private static partial void JsAttachShader(int program, int shader);
    [JSImport("linkProgram", Module)] private static partial void JsLinkProgram(int program);
    [JSImport("deleteProgram", Module)] private static partial void JsDeleteProgram(int program);
    [JSImport("getProgram", Module)] private static partial int JsGetProgram(int program, int name);
    [JSImport("programLog", Module)] private static partial string JsProgramLog(int program);
    [JSImport("useProgram", Module)] private static partial void JsUseProgram(int program);

    [JSImport("getUniformLocation", Module)] private static partial int JsGetUniformLocation(int program, string name);
    [JSImport("uniform1f", Module)] private static partial void JsUniform1f(int location, float value);
    [JSImport("uniform1i", Module)] private static partial void JsUniform1i(int location, int value);

    [JSImport("drawArrays", Module)] private static partial void JsDrawArrays(int mode, int first, int count);
}
