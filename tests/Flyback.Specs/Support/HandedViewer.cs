using Flyback.App;

namespace Flyback.Specs.Support;

/// <summary>A viewer that keeps what View it handed it, for a scenario to open.</summary>
internal sealed class HandedViewer : IViewer
{
    public (string Name, byte[] Bundle)? Handed { get; private set; }

    public void Show(string name, Func<byte[]> pack) => Handed = (name, pack());
}
