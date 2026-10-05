using Avalonia.Controls;
using Avalonia.Media;
using Flyback.Core.Graph;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Gallery;

internal sealed partial class PresetGallery
{
    /// <summary>What a preset is filtered by, beside its words: the half of the Output it works with.</summary>
    private enum Works
    {
        Anything,
        Sound,
        Picture,
    }

    /// <summary>A preset's card, its parts, and what its patch has said of itself so far.</summary>
    private sealed class Card(
        PatchPreset preset,
        Button button,
        Image picture,
        TextBlock words,
        Control speaker,
        TextBlock description,
        Panel badges,
        string section)
    {
        /// <summary>The dot of a preset that is heard.</summary>
        public static Color Heard => Colors.Pattern;

        /// <summary>The dot of a preset that is seen.</summary>
        public static Color Seen => Colors.Form;

        public PatchPreset Preset => preset;

        public Button Button => button;

        public Image Picture => picture;

        /// <summary>What the picture says where there is none to show.</summary>
        public TextBlock Words => words;

        /// <summary>Shown in place of a picture for a preset only heard.</summary>
        public Control Speaker => speaker;

        public TextBlock Description => description;

        public Panel Badges => badges;

        /// <summary>The heading it is listed under.</summary>
        public string Section => section;

        /// <summary>What its patch said of itself, once it has.</summary>
        public Thumbnail? Said { get; set; }

        public IReadOnlyList<string> Tags => Said?.Tags ?? [];

        /// <summary>
        /// Whether it works with <paramref name="kind"/>. Unknown until its patch has
        /// said, and an Output with nothing wired works with both.
        /// </summary>
        public bool WorksWith(Works kind) => kind switch
        {
            Works.Sound => Said?.Reaches is { } wired && (wired.Sound || !wired.Picture),
            Works.Picture => Said?.Reaches is { } wired && (wired.Picture || !wired.Sound),
            _ => true,
        };
    }
}
