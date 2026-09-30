using System.Runtime.InteropServices;
using Flyback.Core.Compile;
using static Flyback.Gpu.GlConstants;

namespace Flyback.Gpu;

/// <summary>
/// A program's tables on the card, one float texture each in rows of
/// <see cref="GlslEmitter.TableRow"/>, for the shader's <c>tab</c> reads.
/// </summary>
/// <remarks>
/// A clip is uploaded once and kept while the program still reads it. A chart's
/// buffer, a Scope's or an Analyzer's, is refilled from the sound every frame, so it
/// is uploaded again each time it is bound.
/// </remarks>
internal sealed class TableTextures
{
    private int[] textures = [];
    private LoadedSample[] held = [];
    private bool[] charts = [];

    private int[] samplers = [];
    private int[] lengths = [];
    private int[] rates = [];

    /// <summary>How many texture units <see cref="Bind"/> takes.</summary>
    public int Count => textures.Length;

    /// <summary>Takes up <paramref name="patch"/>'s tables, uploading those the card does not hold already.</summary>
    public void Adopt(IGl gl, CompiledPatch patch)
    {
        var wanted = patch.Tables;

        charts = [.. wanted.Select(table => patch.Taps.Any(tap => ReferenceEquals(tap.Trace, table)))];

        if (held.Length == wanted.Count)
        {
            var same = true;
            for (var i = 0; i < held.Length; i++) same &= ReferenceEquals(held[i], wanted[i]);

            if (same) return;
        }

        foreach (var texture in textures) gl.DeleteTexture(texture);

        textures = new int[wanted.Count];
        held = [.. wanted];

        for (var i = 0; i < wanted.Count; i++)
        {
            textures[i] = gl.GenTexture();
            gl.BindTexture(GL_TEXTURE_2D, textures[i]);

            // A float texture filters only by extension; the shader interpolates by hand.
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
            gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

            Upload(gl, held[i]);
        }

        gl.BindTexture(GL_TEXTURE_2D, 0);
    }

    /// <summary>Finds where <paramref name="program"/> takes each of its <paramref name="count"/> tables.</summary>
    public void Locate(IGl gl, int program, int count)
    {
        samplers = new int[count];
        lengths = new int[count];
        rates = new int[count];

        for (var i = 0; i < count; i++)
        {
            samplers[i] = gl.GetUniformLocation(program, $"uTable{i}");
            lengths[i] = gl.GetUniformLocation(program, $"uTableLength{i}");
            rates[i] = gl.GetUniformLocation(program, $"uTableRate{i}");
        }
    }

    /// <summary>Binds every table from texture unit <paramref name="unit"/> up, with the charts as the sound last left them.</summary>
    public void Bind(IGl gl, int unit)
    {
        for (var i = 0; i < textures.Length && i < samplers.Length; i++)
        {
            gl.ActiveTexture(GL_TEXTURE0 + unit + i);
            gl.BindTexture(GL_TEXTURE_2D, textures[i]);

            if (charts[i]) Upload(gl, held[i]);

            if (samplers[i] >= 0) gl.Uniform1i(samplers[i], unit + i);
            if (lengths[i] >= 0) gl.Uniform1f(lengths[i], held[i].Samples.Length);
            if (rates[i] >= 0) gl.Uniform1f(rates[i], held[i].SampleRate);
        }
    }

    /// <summary>Hands the textures back.</summary>
    public void Delete(IGl gl)
    {
        foreach (var texture in textures) gl.DeleteTexture(texture);

        Forget();
    }

    /// <summary>Forgets the textures without handing them back, for a context already lost.</summary>
    public void Forget()
    {
        textures = [];
        held = [];
        charts = [];
    }

    /// <summary>Puts <paramref name="table"/> into the texture bound, a row of <see cref="GlslEmitter.TableRow"/> at a time.</summary>
    private static void Upload(IGl gl, LoadedSample table)
    {
        var length = table.Samples.Length;
        var width = Math.Clamp(length, 1, GlslEmitter.TableRow);
        var rows = Math.Max(1, (length + GlslEmitter.TableRow - 1) / GlslEmitter.TableRow);

        // Padded out to whole rows, except where it already fills them, as a chart's buffer does.
        var floats = length == width * rows ? table.Samples : new float[width * rows];
        if (!ReferenceEquals(floats, table.Samples)) table.Samples.CopyTo(floats, 0);

        var pinned = GCHandle.Alloc(floats, GCHandleType.Pinned);

        try
        {
            gl.TexImage2D(GL_TEXTURE_2D, 0, GL_R32F, width, rows, 0, GL_RED, GL_FLOAT, pinned.AddrOfPinnedObject());
        }
        finally
        {
            pinned.Free();
        }
    }
}
