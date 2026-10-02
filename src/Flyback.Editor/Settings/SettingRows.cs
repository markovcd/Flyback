using Flyback.Plugins;

namespace Flyback.Editor.Settings;

/// <summary>What more than one settings section works out the same way.</summary>
internal static class SettingRows
{
    /// <summary>
    /// The row of a list nearest a saved value, so a value written by hand that
    /// the list does not offer shows as the closest one that it does.
    /// </summary>
    public static int Nearest(IReadOnlyList<double> rows, double value) =>
        Enumerable.Range(0, rows.Count).MinBy(row => Math.Abs(rows[row] - value));

    /// <summary>
    /// What a settings tab says about the backend behind it, with the plugin that
    /// offered it named: a machine with two sound plugins installed has nothing
    /// else to say which one these rows belong to.
    /// </summary>
    /// <param name="plugin">
    /// Null for a backend no plugin registered, which leaves the sentence as it
    /// came — an id in brackets that names nothing is worse than no id.
    /// </param>
    public static string Attributed(string what, PluginInfo? plugin) =>
        plugin is null ? $"{what}." : $"{what}, from the {plugin.Name} plugin ({plugin.Id}).";
}
