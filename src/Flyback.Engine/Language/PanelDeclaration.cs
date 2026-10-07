using Flyback.Core.Graph;
using Flyback.Engine.Language.Ast.Expressions;
using Flyback.Engine.Language.Ast.Statements;
using Flyback.Engine.Language.Values;

namespace Flyback.Engine.Language;

/// <summary>A <c>panel</c> line's settings as the knob it puts on the panel.</summary>
internal static class PanelDeclaration
{
    /// <param name="resting">Where the knob rests, from 0 to 1, already checked.</param>
    public static PatchControl? Read(PanelStatement statement, Figure resting, Issues issues)
    {
        var (line, column) = (statement.Line, statement.Column);

        string? label = null;
        string? device = null;
        int? controller = null;
        var channel = 0;
        var held = false;

        foreach (var setting in statement.Settings)
        {
            switch (setting.Name, setting.Value)
            {
                case (null, NameExpr { Name: "held", Port: null }):
                    held = true;
                    break;

                case ("label", TextExpr text):
                    label = text.Value;
                    break;

                case ("device", TextExpr text):
                    device = text.Value;
                    break;

                case ("cc", NumberExpr { Value: >= 0 and <= 127 } number) when number.Value % 1 == 0:
                    controller = (int)number.Value;
                    break;

                case ("channel", NumberExpr { Value: >= 1 and <= 16 } number) when number.Value % 1 == 0:
                    channel = (int)number.Value;
                    break;

                default:
                    issues.Complain(IssueCode.UnknownSetting, setting.Line, setting.Column,
                        "a panel knob takes 'label: \"…\"', 'cc: 0 to 127', 'channel: 1 to 16', 'device: \"…\"' and 'held'.");
                    return null;
            }
        }

        if (controller is null && (device is not null || channel != 0))
        {
            issues.Complain(IssueCode.UnknownSetting, line, column,
                "'device' and 'channel' say which controller 'cc' is, so they come with one.");
            return null;
        }

        if (controller is not null && device is null)
        {
            issues.Complain(IssueCode.UnknownSetting, line, column,
                "a controller is known by its device: add 'device: \"…\"', as the instrument's profile names it.");
            return null;
        }

        return new PatchControl
        {
            Id = NodeIdentity.PanelId(statement.Name),
            Name = label ?? statement.Name,
            Word = label is null || label == statement.Name ? null : statement.Name,
            Value = (float)resting.Amount,
            Midi = controller is { } cc ? new MidiBinding(device!, channel, cc) : null,
            Held = held,
        };
    }
}
