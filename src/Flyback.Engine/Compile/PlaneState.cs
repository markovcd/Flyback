namespace Flyback.Core.Compile;

/// <summary>
/// What a picture carries from one frame to the next for each cycle in the
/// patch: a value per pixel per plane, which is what
/// <see cref="OpCode.PlaneRead"/> reads and <see cref="OpCode.PlaneWrite"/>
/// fills.
/// </summary>
/// <remarks>
/// The picture's <see cref="DelayState"/>. A pixel sees only its own cells, so
/// there is no ping-pong buffer and rows still draw in parallel, unlike
/// <see cref="FeedbackFrame"/>. Laid out pixel-major so each pixel gets one
/// contiguous span. Stored as <see cref="float"/>, since one 1080p plane is
/// already 8 MB (ADR-0074).
/// </remarks>
public sealed class PlaneState
{
    private float[] cells = [];
    private IReadOnlyList<Guid> owners = [];

    /// <summary>How many planes each pixel keeps.</summary>
    public int Count { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>
    /// The cells belonging to the pixel at <paramref name="index"/>, counted in
    /// pixels from the top left, for one run of the program to read and write.
    /// </summary>
    public Span<float> At(int index) => cells.AsSpan(index * Count, Count);

    /// <summary>
    /// Makes this fit <paramref name="program"/> at the given size, keeping what
    /// still belongs to a module the program still has.
    /// </summary>
    /// <remarks>
    /// Kept by owner rather than by slot, so an edit elsewhere in the patch does
    /// not restart a simulation that has been running for a minute — the same
    /// thing <see cref="DelayState.Adopt"/> does for the ear.
    /// <para>
    /// A change of size keeps nothing. A plane is indexed by pixel rather than by
    /// coordinate, which is what spares it a resample and a blur on every pass,
    /// and the price of that is having nothing to resample when the frame is a
    /// different shape.
    /// </para>
    /// </remarks>
    public void Fit(CompiledPatch program, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(program);

        var count = program.PlaneCount;
        var theirs = program.Owners.Planes;
        var sameFrame = width == Width && height == Height;

        // The same program drawing another frame, which is what all but one call
        // in a thousand is. Worth its own answer because the alternative works out
        // an adoption and allocates a map to say nothing changed — sixty times a
        // second, inside the loop that draws.
        // Counted as well as compared, because two programs assembled by hand
        // share one empty list of owners and are not therefore the same program.
        if (sameFrame && count == Count && ReferenceEquals(theirs, owners)) return;

        if (count == 0 || width <= 0 || height <= 0)
        {
            cells = [];
            Count = count;
            Width = width;
            Height = height;
            owners = theirs;
            return;
        }

        var map = sameFrame ? StateOwners.Adopt(theirs, owners) : null;

        if (sameFrame && count == Count)
        {
            Permute(map!);
            owners = theirs;
            return;
        }

        var previous = cells;
        var previousCount = Count;

        cells = new float[Room(count, width, height)];
        Count = count;
        Width = width;
        Height = height;
        owners = theirs;

        if (map is null) return;

        for (var plane = 0; plane < count; plane++)
        {
            var from = map[plane];

            if (from < 0 || from >= previousCount) continue;

            for (int pixel = 0, pixels = width * height; pixel < pixels; pixel++)
                cells[pixel * count + plane] = previous[pixel * previousCount + from];
        }
    }

    /// <summary>Empties every plane, so a loop starts from nothing — what a Rewind asks for.</summary>
    public void Clear() => Array.Clear(cells);

    /// <summary>
    /// Moves each plane to where the new program keeps it, for a recompile that
    /// changed neither the size nor how many planes there are — which is every
    /// knob turned on a patch with a loop in it.
    /// </summary>
    private void Permute(int[] map)
    {
        var moved = false;

        for (var plane = 0; plane < map.Length; plane++)
            if (map[plane] != plane)
                moved = true;

        // The overwhelmingly common case: the same planes in the same order, so
        // the frames already drawn are exactly what the new program wants.
        if (!moved) return;

        var was = new float[Count];

        for (int pixel = 0, pixels = Width * Height; pixel < pixels; pixel++)
        {
            var mine = At(pixel);

            // Read out before any of it is written back, since two planes may
            // swap places.
            mine.CopyTo(was);

            for (var plane = 0; plane < Count; plane++)
            {
                var from = map[plane];

                mine[plane] = from >= 0 && from < was.Length ? was[from] : 0f;
            }
        }
    }

    /// <summary>
    /// How many cells a frame of this shape needs, refusing a size no single
    /// array could hold rather than wrapping round into a smaller one.
    /// </summary>
    private static int Room(int count, int width, int height)
    {
        var room = (long)count * width * height;

        return room <= int.MaxValue
            ? (int)room
            : throw new ArgumentOutOfRangeException(
                nameof(count),
                $"{count} planes at {width}x{height} is more than one array can hold.");
    }
}
