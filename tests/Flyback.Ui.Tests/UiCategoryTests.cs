using Avalonia.Headless.XUnit;
using Flyback.Tests;
using Xunit;

namespace Flyback.Ui.Tests;

/// <summary>A test on the headless UI thread is filed under ui without being marked.</summary>
public class UiCategoryTests
{
    [AvaloniaFact]
    public void A_headless_fact_is_filed_under_ui() => TestCategory.Ui.Carried();

    [AvaloniaTheory]
    [InlineData(1)]
    public void A_headless_theory_is_filed_under_ui(int _) => TestCategory.Ui.Carried();
}
