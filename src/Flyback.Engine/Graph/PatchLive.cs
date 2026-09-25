using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

internal static class PatchLive
{
    extension(Patch patch)
    {
        /// <summary>Writes where every knob rests into a fresh live block, so it does not start at zero.</summary>
        public void Seed(LiveValues block)
        {
            ArgumentNullException.ThrowIfNull(block);

            if (patch.Controls is null) return;

            foreach (var control in patch.Controls) block.Set(control.Key, control.Value);
        }
    }
}
