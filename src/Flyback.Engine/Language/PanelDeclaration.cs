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
        int? note = null;
        var channel = 0;
        var held = false;
        var latching = false;
        var kind = ControlKind.Knob;

        foreach (var setting in statement.Settings)
        {
            switch (setting.Name, setting.Value)
            {
                case (null, NameExpr { Name: "held", Port: null }):
                    held = true;
                    break;

                case (null, NameExpr { Name: "switch", Port: null }):
                    kind = ControlKind.Switch;
                    break;

                case (null, NameExpr { Name: "button", Port: null }):
                    kind = ControlKind.Button;
                    break;

                case (null, NameExpr { Name: "latching", Port: null }):
                    latching = true;
                    break;

                case ("note", NumberExpr { Value: >= 0 and <= 127 } number) when number.Value % 1 == 0:
                    note = (int)number.Value;
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
                        "a panel knob takes 'label: \"…\"', 'cc: 0 to 127', 'note: 0 to 127', 'channel: 1 to 16', 'device: \"…\"', "
                        + "'switch' or 'button', 'latching' and 'held'.");
                    return null;
            }
        }

        if (kind != ControlKind.Knob && resting.Amount is not (0d or 1d))
        {
            issues.Complain(IssueCode.UnknownSetting, line, column, $"a {Word(kind)} rests at 0 for off or 1 for on.");
            return null;
        }

        if (note is not null && (controller is not null || kind == ControlKind.Knob))
        {
            issues.Complain(IssueCode.UnknownSetting, line, column,
                "'note' binds a switch or a button to a pad or a key, in place of 'cc'.");
            return null;
        }

        if (latching && (kind != ControlKind.Switch || controller is null))
        {
            issues.Complain(IssueCode.UnknownSetting, line, column,
                "'latching' says a switch's controller button holds itself down, so it comes with 'switch' and 'cc'.");
            return null;
        }

        controller ??= note;

        if (controller is null && (device is not null || channel != 0))
        {
            issues.Complain(IssueCode.UnknownSetting, line, column,
                "'device' and 'channel' say which controller 'cc' or 'note' is, so they come with one.");
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
            Kind = kind,
            Word = label is null || label == statement.Name ? null : statement.Name,
            Value = (float)resting.Amount,
            Midi = controller is { } bound ? new MidiBinding(device!, channel, bound) { Note = note is not null, Latching = latching } : null,
            Held = held,
        };
    }

    /// <summary>What the text calls <paramref name="kind"/>.</summary>
    public static string Word(ControlKind kind) => kind switch
    {
        ControlKind.Switch => "switch",
        ControlKind.Button => "button",
        _ => "knob",
    };
}
