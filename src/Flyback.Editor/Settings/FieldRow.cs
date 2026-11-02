using Avalonia;
using Avalonia.Controls;

namespace Flyback.Editor.Settings;

/// <summary>
/// A settings row: its name in the gutter and its control beside it, or, while
/// <see cref="StackedProperty"/> is set on a sheet too narrow for both, its name
/// above a control that takes the whole width.
/// </summary>
internal sealed class FieldRow : Grid
{
    /// <summary>Set on a phone's sheet and inherited by every row in it.</summary>
    public static readonly AttachedProperty<bool> StackedProperty =
        AvaloniaProperty.RegisterAttached<FieldRow, Control, bool>("Stacked", inherits: true);

    private readonly string beside;

    /// <summary>Each child's column and width beside the gutter, kept while the row is stacked.</summary>
    private readonly Dictionary<Control, (int Column, double Width)> laid = [];

    /// <param name="beside">The columns after the gutter, as a <see cref="ColumnDefinitions"/> string.</param>
    public FieldRow(string beside)
    {
        this.beside = beside;
        ColumnDefinitions = new ColumnDefinitions($"{InspectorRows.SettingsGutter},{beside}");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StackedProperty) Lay(change.GetNewValue<bool>());
    }

    private void Lay(bool stacked)
    {
        if (stacked == laid.Count > 0) return;

        if (stacked)
        {
            foreach (var child in Children.OfType<Control>()) laid[child] = (GetColumn(child), child.Width);

            ColumnDefinitions = new ColumnDefinitions(beside);
            RowDefinitions = new RowDefinitions("Auto,Auto");

            foreach (var (child, (column, _)) in laid)
            {
                if (column == 0)
                {
                    SetRow(child, 0);
                    SetColumnSpan(child, ColumnDefinitions.Count);
                    child.Width = double.NaN;
                }
                else
                {
                    SetRow(child, 1);
                    SetColumn(child, column - 1);
                }
            }

            return;
        }

        ColumnDefinitions = new ColumnDefinitions($"{InspectorRows.SettingsGutter},{beside}");
        RowDefinitions = [];

        foreach (var (child, (column, width)) in laid)
        {
            SetRow(child, 0);
            SetColumn(child, column);
            SetColumnSpan(child, 1);
            child.Width = width;
        }

        laid.Clear();
    }
}
