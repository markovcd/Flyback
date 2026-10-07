using System.Text.Json.Nodes;

namespace Flyback.Engine.Compile;

/// <summary>
/// Where a program's memory is, as the script <see cref="JsEmitter"/> writes reads it:
/// each array as an element index into the heap view of its type, and the values of its
/// constants.
/// </summary>
/// <remarks>
/// The script works on the arrays themselves rather than on copies, so everything that
/// reads them from this side — a Meter, a Scope, a rewind — sees what it played.
/// Whoever calls this says where each array is: a browser pins them in the runtime's
/// own memory, a test lays them out in a buffer of its own.
/// </remarks>
internal static class JsLayout
{
    /// <param name="address">The byte address of an array's first element, which must be aligned to its element's size.</param>
    public static string Of(
        CompiledPatch program,
        DelayState? memory,
        LiveValues live,
        int sampleRate,
        int oversample,
        Func<Array, long> address)
    {
        var arrays = memory?.Arrays;

        return new JsonObject
        {
            ["sampleRate"] = sampleRate,
            ["oversample"] = oversample,
            ["live"] = Index(live.Storage, sizeof(float), address),
            ["liveCount"] = live.Storage.Length,
            ["lines"] = Indices(arrays?.Lines ?? [], sizeof(float), address),
            ["lineLengths"] = new JsonArray([.. (arrays?.Lines ?? []).Select(line => (JsonNode)line.Length)]),
            ["positions"] = Index(arrays?.Positions, sizeof(int), address),
            ["phases"] = Index(arrays?.Phases, sizeof(double), address),
            ["previous"] = Index(arrays?.PreviousInputs, sizeof(double), address),
            ["running"] = Index(arrays?.Running, sizeof(bool), address),
            ["units"] = Index(arrays?.Units, sizeof(double), address),
            ["planes"] = Index(arrays?.Planes, sizeof(double), address),
            ["traces"] = Indices(arrays?.Traces ?? [], sizeof(float), address),
            ["traceHeads"] = Index(arrays?.TraceHeads, sizeof(int), address),
            ["traceLength"] = DelayState.TraceSamples,

            // JSON has no NaN or infinities, so those go as the text the script reads back with Number.
            ["constants"] = new JsonArray([.. JsEmitter.Constants(program).Select(value => double.IsFinite(value)
                ? (JsonNode)value
                : double.IsNaN(value) ? "NaN" : value > 0 ? "Infinity" : "-Infinity")]),
            ["tables"] = new JsonArray([.. program.TableArray.Select(table => (JsonNode)new JsonArray(
                Index(table.Samples, sizeof(float), address),
                table.Samples.Length,
                table.SampleRate))]),
        }.ToJsonString();
    }

    private static JsonArray Indices(Array[] arrays, int size, Func<Array, long> address) =>
        new([.. arrays.Select(array => (JsonNode)Index(array, size, address))]);

    /// <summary>An element index, or zero for an array that is missing or empty and so never read.</summary>
    private static long Index(Array? array, int size, Func<Array, long> address)
    {
        if (array is null || array.Length == 0) return 0;

        var at = address(array);

        if (at % size != 0)
            throw new ArgumentException($"An array of {size}-byte elements sits at {at}, which is not aligned to them.");

        return at / size;
    }
}
