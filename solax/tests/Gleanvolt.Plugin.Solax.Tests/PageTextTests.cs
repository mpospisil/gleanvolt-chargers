using System.Collections;
using System.Globalization;
using Gleanvolt.Plugin.Solax.Resources;

namespace Gleanvolt.Plugin.Solax.Tests;

public class PageTextTests
{
    [Theory]
    [InlineData("en", "Three phases (X3)")]
    [InlineData("cs", "Tři fáze (X3)")]
    [InlineData("cs-CZ", "Tři fáze (X3)")]
    [InlineData("de", "Three phases (X3)")]   // no German: English
    public void ATextIsInTheAskedLanguageOrEnglish(string culture, string expected) =>
        Assert.Equal(expected, PageText.ResourceManager.GetString(nameof(PageText.ThreePhases), CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void EveryTextHasACzechTranslation()
    {
        var czech = PageText.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo("cs"), true, false)!;
        var keys = PageText.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!
            .Cast<DictionaryEntry>().Select(entry => (string)entry.Key);
        Assert.All(keys, key => Assert.False(string.IsNullOrWhiteSpace(czech.GetString(key)), key));
    }
}
