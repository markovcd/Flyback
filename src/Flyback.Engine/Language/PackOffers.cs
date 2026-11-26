using System.Text.RegularExpressions;
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

        var sound = true;

        foreach (var parameter in module.Parameters.Where(p => p.Default is null))
        {
            issues.Complain(IssueCode.ModuleInputWithoutDefault, parameter.Line, parameter.Column,
                $"'{parameter.Name}' has no default. Every socket of a module has one, so give it a value: {parameter.Name} = 0.");
            sound = false;
        }

        if (sound) offered.Add(module);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex Name();
}
