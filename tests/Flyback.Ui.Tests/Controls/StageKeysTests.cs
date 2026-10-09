using Avalonia.Input;
using Flyback.Ui.Controls;
using Shouldly;
using Xunit;

namespace Flyback.Ui.Tests.Controls;

/// <summary>The keys a picture with the screen answers, which the editor, its picture window and the viewer all read here.</summary>
public class StageKeysTests
{
    [Theory]
    [InlineData(Key.Escape, KeyModifiers.None, StageKey.Leave)]
    [InlineData(Key.Escape, KeyModifiers.Control, StageKey.Leave)]
    [InlineData(Key.F11, KeyModifiers.None, StageKey.FullScreen)]
    [InlineData(Key.F3, KeyModifiers.None, StageKey.Stats)]
    [InlineData(Key.F3, KeyModifiers.Shift, StageKey.Stats)]
    [InlineData(Key.Space, KeyModifiers.None, StageKey.Pause)]
    [InlineData(Key.P, KeyModifiers.Control, StageKey.Pause)]
    [InlineData(Key.P, KeyModifiers.Meta, StageKey.Pause)]
    public void A_stage_key_means_the_same_everywhere(Key key, KeyModifiers modifiers, StageKey meant) =>
        StageKeys.Read(key, modifiers).ShouldBe(meant);

    /// <summary>A bare P is a note, and a command held turns a function key into somebody else's shortcut.</summary>
    [Theory]
    [InlineData(Key.P, KeyModifiers.None)]
    [InlineData(Key.F3, KeyModifiers.Control)]
    [InlineData(Key.F11, KeyModifiers.Alt)]
    [InlineData(Key.Space, KeyModifiers.Control)]
    [InlineData(Key.Z, KeyModifiers.None)]
    public void Anything_else_is_left_to_whoever_wants_it(Key key, KeyModifiers modifiers) =>
        StageKeys.Read(key, modifiers).ShouldBe(StageKey.None);
}
