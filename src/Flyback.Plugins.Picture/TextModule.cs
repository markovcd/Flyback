using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Picture;

/// <summary>
/// Text: lines somebody typed, as the distance to their letters, with a socket
/// that chooses the line and one that types it out.
/// </summary>
/// <remarks>
/// A shape like every other here, so a Fill inks it, a Combine melts it into a
/// circle and a Transform turns it — and nothing downstream knows it was text.
/// <para>
/// The words are carried on the node and never travel down a wire: a patch's
/// wires hold numbers (ADR-0007). They are baked into a <see cref="TextAtlas"/>
/// as the patch is compiled, and what a wire can do is choose where in it to
/// read, which is how 'line' pages through them.
/// </para>
/// <para>
/// 'size' is the height of a capital, the number a person means by how big text
/// is, in whichever font is chosen — so changing the font changes the letters
/// and not how big they are. The tails of g and y hang below it, and the line is centered on its
/// capitals rather than on its tails, so a caption does not jump when a y
/// arrives.
/// </para>
/// </remarks>
internal static class TextModule
{
    public const string TypeId = "flyback.picture.text";

    /// <summary>The socket that chooses the line.</summary>
    public const int LinePort = 3;

    /// <summary>The socket that types the line out.</summary>
    public const int RevealPort = 4;

    /// <summary>What a Text says as it is dropped on the canvas.</summary>
    public const string Greeting = "Hello";

    private const string StateKey = "text";
    private const string LinesKey = "lines";
    private const string FontKey = "font";

    /// <summary>What the distance reads where there is no text at all: nowhere near.</summary>
    private const float Far = 2f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Text", ModuleCategories.Forms,
        [
            ..Field.Position(),
            Field.Size("size", 0.2f) with { Help = "A capital's height." },
            new PortSpec("line", PortKind.Scalar, 0f, 0f, 16f, Display: PortDisplay.Integer)
            {
                Help = "Which line shows. Wraps past the last, so a counter pages through them.",
            },
            new PortSpec("reveal", PortKind.Scalar, 1f, 0f, 1f)
            {
                Help = "Types the line out from the left as it goes from 0 to 1.",
            },
        ],
        [Field.Distance("distance")],
        Emit,
        "Lines of text in a pixel font, as the distance to the letters: patch it into a Fill. "
        + "The lines and the font (Pixel, or the blockier one-case Tiny) are set on the node.")
    {
        Extras = [new LinesExtra()],
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Forms))
        {
            Glyph = "M5,20 L11,4 L17,20 M7.4,14 L14.6,14",
        },
    };

    /// <summary>
    /// Sets the lines an instance shows, for a preset assembling one in code —
    /// what typing into the panel does, said once so the two cannot disagree.
    /// </summary>
    public static NodeInstance WithLines(NodeInstance node, params IEnumerable<string> lines)
    {
        var extra = Definition.Extra<LinesExtra>() ?? new LinesExtra();

        var held = extra.Stored(node.StateOf(StateKey));
        held[LinesKey] = extra.Fields[0].Sane(JsonValue.Create(string.Join('\n', lines)));

        node.SetState(StateKey, held);

        return node;
    }

    /// <summary>Sets the font an instance draws in.</summary>
    public static NodeInstance WithFont(NodeInstance node, BitmapFont font)
    {
        var extra = Definition.Extra<LinesExtra>() ?? new LinesExtra();

        var held = extra.Stored(node.StateOf(StateKey));
        held[FontKey] = JsonValue.Create(font.Id);

        node.SetState(StateKey, held);

        return node;
    }

    /// <summary>
    /// The words, carried as one field of several lines and a choice of font,
    /// and folded onto the context already baked.
    /// </summary>
    /// <remarks>
    /// Baked at both sinks rather than only where the picture is drawn: reading
    /// a texture is arithmetic to the program that plays, so a Text swept by a
    /// loop is heard as the shape of its letters, which is what every other
    /// shape here does too.
    /// </remarks>
    private sealed record LinesExtra : NodeExtra
    {
        public override string Key => StateKey;

        public override IReadOnlyList<ExtraField> Fields =>
        [
            new ExtraField.Text(LinesKey, "lines", Greeting, Multiline: true),
            new ExtraField.Choice(FontKey, "font", Fonts, BitmapFont.Pixel.Id),
        ];

        private static IReadOnlyList<ChoiceOption> Fonts { get; } =
            [.. BitmapFont.All.Select(font => new ChoiceOption(font.Id, font.Name))];

        public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env)
        {
            var state = new ExtraState(Fields, node.StateOf(StateKey));
            var chosen = state.Chosen(FontKey);

            // Kept as chosen rather than corrected, as a choice is: a patch written
            // by a build with a font this one lacks draws in Pixel here and says
            // so, and still names its own font when it is saved again.
            if (BitmapFont.Find(chosen) is not { } font)
            {
                font = BitmapFont.Pixel;

                env.Report(new CompileIssue(
                    node.Id,
                    $"'{env.Title}' asks for a font called '{chosen}', which this build does not "
                    + $"have, so it is drawn in {font.Name}.",
                    IssueSeverity.Warning));
            }

            if (TextAtlas.Of(font, state.Text(LinesKey)) is not { } atlas) return ctx;

            if (atlas.Cut)
            {
                env.Report(new CompileIssue(
                    node.Id,
                    $"'{env.Title}' shows its first {TextAtlas.MostLines} lines and the first "
                    + $"{TextAtlas.MostColumns} letters of each.",
                    IssueSeverity.Warning));
            }

            return ctx.With(StateKey, atlas);
        }
    }

    /// <remarks>
    /// Everything is worked in font pixels, with the middle of the widest line's
    /// capitals at nought, and turned back into the picture's units at the end.
    /// The atlas answers where the letters are; a box around the ink answers
    /// everywhere past it, which is what keeps the black outside the picture
    /// from reading as ink.
    /// </remarks>
    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        if (node.Extra<TextAtlas>(StateKey) is not { } atlas) return [em.Constant(Far)];

        var font = atlas.Font;

        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        var pixel = em.Mul(em.Binary(OpCode.Max, node[2], em.Constant(1e-3f)), 1f / font.Cap);
        var across = em.Binary(OpCode.Div, node[0], pixel);
        var up = em.Binary(OpCode.Div, node[1], pixel);

        // Floored first, so a line held at 1.9 is line 1 rather than halfway to
        // the next band; floored modulo, so counting down wraps as counting up does.
        var row = em.Binary(OpCode.Mod, em.Unary(OpCode.Floor, node[LinePort]), em.Constant(atlas.Lines));

        // From the middle of the capitals to the band's own corner, then into
        // the coordinates a picture is read at — see LoadedImage.At.
        var halfInk = atlas.Ink / 2f;
        var inBand = em.Add(across, halfInk + TextAtlas.Margin);
        var downBand = em.Add(em.Mul(row, atlas.Band), em.Sub(em.Constant(TextAtlas.Margin + font.Cap / 2f), up));

        var scale = 2f * TextAtlas.Texels / atlas.Image.Height;
        var x = em.Add(em.Mul(inBand, scale), -(float)atlas.Image.Width / atlas.Image.Height);
        var y = em.Sub(one, em.Mul(downBand, scale));

        var read = em.Picture(x, y, atlas.Image);
        var near = em.Mul(em.Add(Slot.Scalar(read.Component(0)), -0.5f), 2f * TextAtlas.Reach);
        var threshold = Slot.Scalar(read.Component(1));

        // A letter not yet typed is pushed out past anything its own distance
        // could say, which leaves the ones before it exactly as they were.
        var hidden = em.Sub(one, em.Binary(OpCode.Step, threshold, node[RevealPort]));
        var typed = em.Add(near, em.Mul(hidden, 2f * TextAtlas.Reach));

        // The box the ink sits in: from the tops of the capitals to the ends of
        // the tails, and as wide as the widest line.
        var middle = (font.Height - font.Cap) / 2f;
        var halfHeight = font.Height / 2f;
        var outX = em.Sub(em.Unary(OpCode.Abs, across), em.Constant(halfInk));
        var outY = em.Sub(em.Unary(OpCode.Abs, em.Add(up, middle)), em.Constant(halfHeight));
        var box = em.Add(
            em.Binary(OpCode.Hypot, em.Binary(OpCode.Max, outX, zero), em.Binary(OpCode.Max, outY, zero)),
            em.Binary(OpCode.Min, em.Binary(OpCode.Max, outX, outY), zero));

        return [em.Mul(em.Binary(OpCode.Max, typed, box), pixel)];
    }
}
