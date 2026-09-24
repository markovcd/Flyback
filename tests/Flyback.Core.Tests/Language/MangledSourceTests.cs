using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// A patch in the source view is half-written most of the time it is read: the
/// editor builds on every keystroke. Every one of those has to come back as
/// issues with a code, never as an exception.
/// </summary>
/// <remarks>
/// Random single-character edits to sources that do parse, which is what a
/// half-typed line actually is — where <see cref="LanguageTests"/> pins what the
/// language means, this pins that nothing it cannot mean gets out of the reader.
/// Anything the reader does accept is taken the rest of the way, since a patch
/// that binds and then falls over in the compiler is the same crash one step
/// later.
/// </remarks>
public class MangledSourceTests
{
    private static readonly string[] Sources =
    [
        "x |> sine(freq: 1.5) |> color.hsv(hue: _, saturation: 0.85, value: 1) |> out.color",
        "let slow = sine(freq: 0.15, amp: 0.5, bias: 0.5)\nsine(freq: 110) * slow |> out.left",
        "(x * 2 - 1) * (y + 0.5) - -x / 3 |> out.color",
        "rotate(angle: t * 0.15) |> kaleidoscope(segments: 6) |> out.color",
        "seq(steps: [0 3 7 12], rate: 4) |> out.left",
    ];

    private static readonly HashSet<string> Codes = [.. typeof(IssueCode)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Select(field => (string)field.GetValue(null)!)];

    [Fact]
    public void A_mangled_source_is_a_complaint_rather_than_a_crash()
    {
        var random = new Random(20260920);
        var failures = new List<string>();

        foreach (var source in Sources)
        {
            for (var i = 0; i < 4000; i++)
            {
                var mangled = Mangle(source, random);

                try
                {
                    var load = PatchLanguage.Build(mangled, NodeCatalog.BuiltIn);

                    if (load.Issues.FirstOrDefault(issue => !Codes.Contains(issue.Code)) is { } uncoded)
                        failures.Add($"no code: {uncoded}{Environment.NewLine}{mangled}");

                    if (load.Issues.Count > 0) continue;

                    var program = load.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
                    program.Evaluate(0.25d, -0.5d, 3d, program.AllocateRegisters(), default);
                }
                catch (Exception e)
                {
                    failures.Add($"{e.GetType().Name}: {e.Message}{Environment.NewLine}{mangled}");
                }
            }
        }

        failures.ShouldBeEmpty(
            string.Join(Environment.NewLine + "---" + Environment.NewLine, failures.Distinct().Take(5)));
    }

    /// <summary>A few edits to a source: a character cut, dropped in, swapped, the tail taken off, or a line said twice.</summary>
    private static string Mangle(string source, Random random)
    {
        const string alphabet = "()[]{}|><:,.\"'-+*/ \t\r\n0123456789abcxyzt_=#";

        var text = source;

        for (var edits = 1 + random.Next(3); edits > 0; edits--)
        {
            if (text.Length == 0) break;

            var at = random.Next(text.Length);

            text = random.Next(5) switch
            {
                0 => text.Remove(at, 1),
                1 => text.Insert(at, alphabet[random.Next(alphabet.Length)].ToString()),
                2 => text[..at] + alphabet[random.Next(alphabet.Length)] + text[(at + 1)..],
                3 => text[..at],
                _ => Again(text, random),
            };
        }

        return text;
    }

    private static string Again(string text, Random random)
    {
        var lines = text.Split('\n');

        return text + '\n' + lines[random.Next(lines.Length)];
    }
}
