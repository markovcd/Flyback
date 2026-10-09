namespace Flyback.Plugins.Audio;

/// <summary>An opened input, as a <see cref="BlockReader"/> reads it.</summary>
/// <param name="Block">Where each read lands, allocated so the thread never does.</param>
/// <param name="Read">Fills the block and returns how many samples it holds, or a negative number once the device is gone.</param>
/// <param name="Close">Lets go of the device, on the reading thread.</param>
internal readonly record struct BlockSource(float[] Block, Func<float[], int> Read, Action Close);
