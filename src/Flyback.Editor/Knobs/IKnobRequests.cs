using Flyback.Core.Graph;

namespace Flyback.Editor.Knobs;

/// <summary>What the knob panel asks to be done to the patch's knobs, each by the knob's id.</summary>
internal interface IKnobRequests
{
    /// <summary>Another knob.</summary>
    void Add();

    /// <summary>Start linking sockets to this knob, or stop.</summary>
    void ToggleLink(Guid id);

    /// <summary>Learn the controller this knob follows; with <paramref name="onward"/>, each knob after it in turn.</summary>
    void Learn(Guid id, bool onward);

    /// <summary>Forget the controller this knob follows.</summary>
    void Forget(Guid id);

    /// <summary>Follow this controller, picked from the list of instruments.</summary>
    void Bind(Guid id, MidiBinding binding);

    void Rename(Guid id, string name);

    void Remove(Guid id);

    /// <summary>Play the knob as a knob, a switch or a button.</summary>
    void PlayAs(Guid id, ControlKind kind);

    /// <summary>Have the knob's sockets follow it in decades, or evenly.</summary>
    void SweepInDecades(Guid id, bool inDecades);

    /// <summary>Have the knob sit out randomizing, or take part again.</summary>
    void Hold(Guid id, bool held);

    /// <summary>Move the knob to the place it should have once taken out of its old one.</summary>
    void Move(Guid id, int index);

    /// <summary>Trade the two knobs' places.</summary>
    void Swap(Guid first, Guid second);
}
