using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Flyback.Engine.Compile;

namespace Flyback.Engine.Render;

/// <summary>
/// One renderer's sound cut into legs and run on several cores for a buffer at a time
/// (ADR-0230): each leg runs a stretch of evaluations, then hands what it worked out to
/// the next leg, which runs that stretch while the first goes on to the one after.
/// </summary>
/// <remarks>
/// The legs are the program's own ops in its own order, so the samples are the ones one
/// core would make, to the bit. A leg is taken by whichever thread finds it ready, the
/// rendering thread among them: a stretch waits only on one being run elsewhere, never on
/// a helper waking up. Sized off the sound's thread for one shape of program, and
/// allocation-free once built.
/// </remarks>
internal sealed class SoundRelay
{
    /// <summary>Evaluations in one stretch: long enough that handing on costs little, short enough that legs overlap.</summary>
    internal const int Stretch = 128;

    /// <summary>How many stretches a leg may run ahead of the next, which is how many hand-offs it keeps.</summary>
    private const int Ahead = 8;

    /// <summary>The most frames one round renders; a longer buffer is several rounds.</summary>
    internal const int Frames = 4096;

    private readonly SoundLegs legs;
    private readonly int registerCount;
    private readonly double[][] banks;
    private readonly double[][] handoffs;
    private readonly double[][] settledHandoffs;
    private readonly LiveValues snapshot;
    private readonly double[] times;
    private readonly float[] evaluated;
    private readonly int[] done;
    private readonly int[] busy;
    private readonly int wanted;
    private readonly DelayState[] legStates;
    private readonly DelayState?[] legDelays;

    private IlProgram program = null!;
    private LiveValues? live;
    private double aspect;
    private int left;
    private int right;
    private int evaluations;
    private int stretches;
    private int round;
    private int generation = -1;
    private int inside;
    private Exception? fault;

    public SoundRelay(IlProgram il, int oversample)
    {
        legs = il.Legs ?? throw new ArgumentException("The program is not cut into legs.", nameof(il));
        Methods = il.Methods;
        Oversample = oversample;
        registerCount = Math.Max(il.Source.RegisterCount, il.Source.OutputWidth);

        banks = [.. Enumerable.Range(0, legs.Count).Select(_ => new double[registerCount])];
        handoffs = [.. legs.Handed.Select(handed => new double[Ahead * Stretch * handed.Length])];
        settledHandoffs = [.. legs.Settled.Select(settled => new double[settled.Length])];
        snapshot = new LiveValues(il.Source.LiveInputs);
        times = new double[Frames * oversample];
        evaluated = new float[Frames * oversample * 2];
        done = new int[legs.Count];
        busy = new int[legs.Count];
        wanted = legs.Count - 1;
        legStates = [.. Enumerable.Range(0, legs.Count).Select(_ => DelayState.ForLeg(il.Source))];
        legDelays = new DelayState?[legs.Count];

        RelayCrew.Hire(legs.Count - 1);
    }

    /// <summary>The shape this was sized for; a program of another shape needs a relay of its own.</summary>
    public IlMethods Methods { get; }

    public int Oversample { get; }

    /// <summary>Whether this can run <paramref name="il"/>.</summary>
    public bool Fits(IlProgram il, int oversample) => ReferenceEquals(il.Methods, Methods) && oversample == Oversample;

    /// <summary>
    /// Evaluates <paramref name="count"/> frames' worth of the sound at the times one core
    /// would, left and right a pair per evaluation, starting from the registers in
    /// <paramref name="bank"/> and leaving them there as one core would have.
    /// </summary>
    public ReadOnlySpan<float> Run(
        IlProgram il,
        double[] bank,
        double time,
        double innerStep,
        double outerStep,
        int count,
        DelayState? lines,
        LiveValues? values,
        double ratio)
    {
        var total = count * Oversample;

        for (int frame = 0, at = 0; frame < count; frame++, time += outerStep)
            for (var k = 0; k < Oversample; k++, at++)
                times[at] = time + k * innerStep;

        for (var l = 0; l < banks.Length; l++)
        {
            Array.Copy(bank, banks[l], registerCount);
            il.FillConstants(banks[l]);
        }

        // Each leg on cells of its own, so two cores never write one cache line; the
        // shared state takes back what each owns once the round is over.
        var lent = lines is not null;
        for (var l = 0; l < legStates.Length && lent; l++) lent = lines!.Lend(legStates[l]);
        for (var l = 0; l < legDelays.Length; l++) legDelays[l] = lent ? legStates[l] : lines;

        program = il;

        // One moment for every leg: what is played in while the legs run is heard from the next buffer.
        if (values is not null && values.Count == snapshot.Count)
        {
            values.CopyTo(snapshot.Storage);
            live = snapshot;
        }
        else
        {
            live = values;
        }

        aspect = ratio;
        left = il.Source.OutputBase;
        right = il.Source.OutputWidth > 1 ? left + 1 : left;
        evaluations = total;
        stretches = (total + Stretch - 1) / Stretch;
        fault = null;

        // Counters before the generation, in leg order, so a helper that sees this
        // round's generation sees every counter reset with it.
        for (var l = 0; l < done.Length; l++) Volatile.Write(ref done[l], 0);
        var generationNow = ++round & int.MaxValue;
        Volatile.Write(ref generation, generationNow);

        var posted = RelayCrew.Post(this, wanted);
        var spinner = new SpinWait();

        while (Volatile.Read(ref done[^1]) < stretches)
        {
            if (TryRun(generationNow)) spinner.Reset();
            else spinner.SpinOnce(sleep1Threshold: -1);
        }

        Volatile.Write(ref generation, -1);
        if (posted) RelayCrew.Withdraw(this);

        for (var l = 0; l < done.Length; l++) _ = Volatile.Read(ref done[l]);

        if (fault is { } thrown) ExceptionDispatchInfo.Throw(thrown);

        if (lent)
            for (var l = 0; l < legStates.Length; l++)
                lines!.TakeBack(legStates[l], legs.Cells[l]);

        // What one core would have left in its bank: each register as the last leg to write it left it.
        for (var l = 0; l < banks.Length; l++)
            foreach (var register in legs.Written[l])
                bank[register] = banks[l][register];

        return evaluated.AsSpan(0, total * 2);
    }

    /// <summary>Takes legs of the posted round while there are any; true if it ran one.</summary>
    public bool Help()
    {
        var at = Volatile.Read(ref generation);
        if (at < 0) return false;

        if (Interlocked.Increment(ref inside) > wanted)
        {
            Interlocked.Decrement(ref inside);
            return false;
        }

        var ran = false;
        var spinner = new SpinWait();

        try
        {
            while (Volatile.Read(ref generation) == at && Volatile.Read(ref done[^1]) < Volatile.Read(ref stretches))
            {
                if (TryRun(at))
                {
                    ran = true;
                    spinner.Reset();
                }
                else
                {
                    spinner.SpinOnce(sleep1Threshold: -1);
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref inside);
        }

        return ran;
    }

    private bool TryRun(int at)
    {
        for (var leg = done.Length - 1; leg >= 0; leg--)
        {
            if (Volatile.Read(ref busy[leg]) != 0 || !Ready(leg, out _)) continue;
            if (Interlocked.CompareExchange(ref busy[leg], 1, 0) != 0) continue;

            // Checked again once held, and the round with it: a counter seen from the
            // next round reads a generation that is no longer this one.
            if (!Ready(leg, out var stretch) || Volatile.Read(ref generation) != at)
            {
                Volatile.Write(ref busy[leg], 0);
                continue;
            }

            try
            {
                RunStretch(leg, stretch);
                Volatile.Write(ref done[leg], stretch + 1);
            }
            catch (Exception thrown)
            {
                fault = thrown;
                for (var l = 0; l < done.Length; l++) Volatile.Write(ref done[l], int.MaxValue);
            }
            finally
            {
                Volatile.Write(ref busy[leg], 0);
            }

            return true;
        }

        return false;
    }

    /// <summary>Whether <paramref name="leg"/>'s next stretch has what it reads and somewhere to put what it hands on.</summary>
    private bool Ready(int leg, out int stretch)
    {
        stretch = Volatile.Read(ref done[leg]);
        if (stretch >= Volatile.Read(ref stretches)) return false;
        if (leg > 0 && Volatile.Read(ref done[leg - 1]) <= stretch) return false;
        return leg == done.Length - 1 || stretch - Volatile.Read(ref done[leg + 1]) < Ahead;
    }

    private void RunStretch(int leg, int stretch)
    {
        var bank = banks[leg];
        var state = legDelays[leg];
        ref var registers = ref MemoryMarshal.GetArrayDataReference(bank);
        ReadOnlySpan<int> taken = leg > 0 ? legs.Handed[leg - 1] : [];
        ReadOnlySpan<int> given = leg < done.Length - 1 ? legs.Handed[leg] : [];
        var from = leg > 0 ? handoffs[leg - 1] : null;
        var into = leg < done.Length - 1 ? handoffs[leg] : null;
        ReadOnlySpan<int> settledTaken = leg > 0 ? legs.Settled[leg - 1] : [];
        ReadOnlySpan<int> settledGiven = leg < done.Length - 1 ? legs.Settled[leg] : [];
        var last = stretch * Stretch + Stretch;
        if (last > evaluations) last = evaluations;

        for (var e = stretch * Stretch; e < last; e++)
        {
            var slot = e % (Ahead * Stretch);

            if (from is not null)
                for (int i = 0, at = slot * taken.Length; i < taken.Length; i++, at++)
                    bank[taken[i]] = from[at];

            if (e == 0)
                for (var i = 0; i < settledTaken.Length; i++)
                    bank[settledTaken[i]] = settledHandoffs[leg - 1][i];

            program.EvaluateLeg(leg, times[e], ref registers, state, aspect, live);

            if (e == 0)
                for (var i = 0; i < settledGiven.Length; i++)
                    settledHandoffs[leg][i] = bank[settledGiven[i]];

            if (into is not null)
            {
                for (int i = 0, at = slot * given.Length; i < given.Length; i++, at++)
                    into[at] = bank[given[i]];
            }
            else
            {
                evaluated[e * 2] = (float)bank[left];
                evaluated[e * 2 + 1] = (float)bank[right];
            }
        }
    }
}
