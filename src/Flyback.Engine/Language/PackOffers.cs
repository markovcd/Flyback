using System.Text.RegularExpressions;
using Flyback.Engine.Language.Ast;
using Flyback.Engine.Language.Ast.Expressions;
using Flyback.Engine.Language.Ast.Statements;

namespace Flyback.Engine.Language;

/// <summary>
/// The <c>module</c> defs a pack offers, each checked as it is read: written at the top of
/// a pack, named in lower case, and every parameter with a default, as every socket has one.
/// </summary>
internal sealed partial class PackOffers(Issues issues)
{
    private readonly List<DefStatement> offered = [];

    /// <summary>The modules offered, in the order written; their cards are read from the source.</summary>
    public IReadOnlyList<DefStatement> Offered => offered;

    /// <summary>Checks a <c>module</c> where it is read; <paramref name="top"/> says it is outside every block.</summary>
    public void Offer(DefStatement module, PackHeader? pack, bool top)
    {
        if (!top)
        {
            issues.Complain(IssueCode.ModuleNotAtTop, module.Line, module.Column,
                $"'{module.Name}' is a module its pack offers, so it is written at the top of the file, outside every group, def, loop and if. Write 'def' for one used only here.");
            return;
        }

        if (pack is null)
        {
            issues.Complain(IssueCode.ModuleOutsidePack, module.Line, module.Column,
                $"'{module.Name}' is a module, which a pack offers: open the file with 'pack <id> <version>', or write 'def' for one used only here.");
            return;
        }

        if (!Name().IsMatch(module.Name))
        {
            issues.Complain(IssueCode.BadModuleName, module.Line, module.Column,
                $"'{module.Name}' is not a module's name. It is lower-case letters, digits and '_', starting with a letter, as in 'module tape_echo(...)'.");
            return;
        }

        var sound = module.Parameters.Aggregate(true, (ok, parameter) => Checked(parameter) && ok);
        sound = Named(module) && sound;
        var settings = module.Parameters.Select(p => p.Within is ListExpr list ? list.Items.Count : 1).Aggregate(1L, (a, b) => a * b);

        if (sound && settings > MostSettings)
        {
            issues.Complain(IssueCode.ModuleTooManyChoices, module.Line, module.Column,
                $"'{module.Name}' can be set {settings} ways, and a module is built once for each: at most {MostSettings}. Offer fewer choices.");
            sound = false;
        }

        if (sound) offered.Add(module);
    }

    /// <summary>Whether every output a module names has a name of its own.</summary>
    private bool Named(DefStatement module)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var sound = true;

        foreach (var output in module.Named)
        {
            if (taken.Add(output.Name)) continue;

            issues.Complain(IssueCode.ModuleDuplicateOutput, output.Line, output.Column,
                $"'{module.Name}' already has an output called '{output.Name}'. Give each 'out' a name of its own.");
            sound = false;
        }

        return sound;
    }

    /// <summary>The most ways a module's choices may combine, since each is built when the pack loads.</summary>
    public const int MostSettings = 64;

    /// <summary>The most choices one setting offers.</summary>
    public const int MostChoices = 32;

    private bool Checked(Parameter parameter)
    {
        if (parameter.Default is not { } fallback)
        {
            issues.Complain(IssueCode.ModuleInputWithoutDefault, parameter.Line, parameter.Column,
                $"'{parameter.Name}' has no default. Every socket of a module has one, so give it a value: {parameter.Name} = 0.");
            return false;
        }

        if (parameter.Within is ListExpr choices) return Chosen(parameter.Name, fallback, choices);

        if (Number(fallback) is not { } value)
        {
            issues.Complain(IssueCode.ModuleDefaultNotANumber, fallback.Line, fallback.Column,
                $"'{parameter.Name}' is a socket, so its default is a number, a note or a duration, or arithmetic on those. A text is one of a setting's choices: {parameter.Name} = \"a\" in [\"a\", \"b\"].");
            return false;
        }

        if (parameter.Within is not RangeExpr range) return true;

        if (Number(range.Low) is not { } low || Number(range.High) is not { } high || low >= high)
        {
            issues.Complain(IssueCode.ModuleBadRange, range.Line, range.Column,
                $"'{parameter.Name}' turns through a range of two numbers, the lower first: {parameter.Name} = 0 in 0..1.");
            return false;
        }

        if (value < low || value > high)
        {
            issues.Complain(IssueCode.ModuleDefaultOutsideRange, fallback.Line, fallback.Column,
                $"'{parameter.Name}' rests at {value}, outside the {low}..{high} it turns through.");
            return false;
        }

        return true;
    }

    private bool Chosen(string name, Expr fallback, ListExpr choices)
    {
        var options = Options(choices);

        if (options is null || options.Count < 2 || options.Count > MostChoices || options.Distinct().Count() != options.Count)
        {
            issues.Complain(IssueCode.ModuleBadChoices, choices.Line, choices.Column,
                $"'{name}' offers 2 to {MostChoices} different choices, all numbers or all texts: {name} = 16 in [16, 32, 64].");
            return false;
        }

        if (Option(fallback) is not { } chosen || !options.Contains(chosen))
        {
            issues.Complain(IssueCode.ModuleDefaultOutsideRange, fallback.Line, fallback.Column,
                $"'{name}' starts at one of its choices: {string.Join(", ", options.Select(o => o.Text ? $"\"{o.Value}\"" : o.Value))}.");
            return false;
        }

        return true;
    }

    /// <summary>Each of a setting's choices as written, or null where they are not all numbers or all texts.</summary>
    internal static IReadOnlyList<(string Value, bool Text)>? Options(ListExpr choices)
    {
        var options = choices.Items.Select(Option).ToList();

        return options.All(o => o is { Text: true }) || options.All(o => o is { Text: false })
            ? [.. options.Select(o => o!.Value)]
            : null;
    }

    /// <summary>One choice as written: a text without its quotes, or a number in the invariant form.</summary>
    internal static (string Value, bool Text)? Option(Expr expr) => expr switch
    {
        TextExpr text => (text.Value, true),
        _ when Number(expr) is { } number => (number.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
        _ => null,
    };

    /// <summary>What a socket's default is worth, or null where it is not arithmetic on numbers.</summary>
    internal static double? Number(Expr? expr) => expr switch
    {
        NumberExpr number => number.Value,
        NegateExpr { Value: var inner } => -Number(inner),
        BinaryExpr { Operator: var op, Left: var left, Right: var right } when Number(left) is { } a && Number(right) is { } b => op switch
        {
            TokenKind.Plus => a + b,
            TokenKind.Minus => a - b,
            TokenKind.Star => a * b,
            TokenKind.Slash when b != 0 => a / b,
            _ => null,
        },
        _ => null,
    };

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex Name();
}
