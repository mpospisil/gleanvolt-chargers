using System.Collections;
using System.Globalization;
using Gleanvolt.Plugin.GoE.Resources;

namespace Gleanvolt.Plugin.GoE.Tests;

public class PageTextTests
{
    [Theory]
    [InlineData("en", "Force state (frc)")]
    [InlineData("cs", "Vynucený stav (frc)")]
    [InlineData("cs-CZ", "Vynucený stav (frc)")]
    [InlineData("de", "Force state (frc)")]   // no German: English
    public void ATextIsInTheAskedLanguageOrEnglish(string culture, string expected) =>
        Assert.Equal(expected, PageText.ResourceManager.GetString(nameof(PageText.ForceState), CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void EveryTextHasACzechTranslation()
    {
        var czech = PageText.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo("cs"), true, false);
        Assert.NotNull(czech); // a missing Czech satellite assembly fails here, readably
        var keys = PageText.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!
            .Cast<DictionaryEntry>().Select(entry => (string)entry.Key);
        Assert.All(keys, key => Assert.False(string.IsNullOrWhiteSpace(czech.GetString(key)), key));
    }
}
