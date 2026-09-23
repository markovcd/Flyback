using Flyback.Core.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// <see cref="Emitter.Once"/>: part of a module lowered once however many places
/// a sweep reads the module at, wherever it would read the same.
/// </summary>
public class OnceTests
{
    private static Slot[] Ring(Emitter em, Slot input)
    {
        var plane = em.AllocatePlaneSlot();
        var held = em.Add(em.PlaneRead(plane), input);

        em.PlaneWrite(plane, held);
        return [em.Mul(held, em.Load(OpCode.LoadT))];
    }

    [Fact]
    public void Lowered_again_under_a_place_it_never_read_it_is_the_same_registers_and_the_same_plane()
    {
        var em = new Emitter();
        var input = em.Constant(0.5f);
        var now = em.Load(OpCode.LoadT);

        em.PushDomain(em.Constant(-1f), em.Constant(0f), now);
        var first = em.Once("ring", [input], () => Ring(em, input));
        em.PopDomain();

        em.PushDomain(em.Constant(1f), em.Constant(0f), now);
        var second = em.Once("ring", [input], () => Ring(em, input));
        em.PopDomain();

        second.ShouldBe(first);
        em.PlaneSlotCount.ShouldBe(1);
    }

    [Fact]
    public void A_moment_it_did_read_is_lowered_again()
    {
        var em = new Emitter();
        var input = em.Constant(0.5f);

        var now = em.Once("ring", [input], () => Ring(em, input));

        em.PushDomain(em.Load(OpCode.LoadX), em.Load(OpCode.LoadY), em.Constant(2f));
        var then = em.Once("ring", [input], () => Ring(em, input));
        em.PopDomain();

        then.ShouldNotBe(now);
        em.PlaneSlotCount.ShouldBe(2);
    }

    [Fact]
    public void So_is_a_different_input()
    {
        var em = new Emitter();
        var soft = em.Constant(0.5f);
        var loud = em.Constant(1f);

        var first = em.Once("ring", [soft], () => Ring(em, soft));
        var second = em.Once("ring", [loud], () => Ring(em, loud));

        second.ShouldNotBe(first);
    }
}
