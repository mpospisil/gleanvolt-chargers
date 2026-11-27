using System.Collections;
using System.Globalization;
using Gleanvolt.Plugin.Huawei.Resources;

namespace Gleanvolt.Plugin.Huawei.Tests;

public class PageTextTests
{
    [Theory]
    [InlineData("en", "Phase voltages")]
    [InlineData("cs", "Napětí fází")]
    [InlineData("cs-CZ", "Napětí fází")]
    [InlineData("de", "Phase voltages")]   // no German: English
    public void ATextIsInTheAskedLanguageOrEnglish(string culture, string expected) =>
        Assert.Equal(expected, PageText.ResourceManager.GetString(nameof(PageText.Voltages), CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void EveryTextHasACzechTranslation()
    {
        var czech = PageText.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo("cs"), true, false)!;
        var keys = PageText.ResourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!
            .Cast<DictionaryEntry>().Select(entry => (string)entry.Key);
        Assert.All(keys, key => Assert.False(string.IsNullOrWhiteSpace(czech.GetString(key)), key));
    }
}
