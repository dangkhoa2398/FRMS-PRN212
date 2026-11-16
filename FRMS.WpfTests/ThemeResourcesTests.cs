using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FRMS.PRN212.WpfConnection.Theming;
using Xunit;

namespace FRMS.WpfTests;

public sealed class ThemeResourcesTests
{
    [Fact]
    public Task LightAndDarkThemes_ShareEveryPaletteKeyAndUseDistinctColors() => StaTest.RunAsync(() =>
    {
        var light = Load("Light");
        var dark = Load("Dark");
        var keys = light.Keys.Cast<object>().ToArray();

        Assert.Equal(keys.Length, dark.Keys.Count);
        Assert.True(keys.Length >= 20);
        foreach (var key in keys)
        {
            Assert.True(dark.Contains(key), $"Theme is missing shared resource: {key}");
            Assert.IsType<SolidColorBrush>(light[key]);
            Assert.IsType<SolidColorBrush>(dark[key]);
        }

        var lightBackground = Assert.IsType<SolidColorBrush>(light["PageBackgroundBrush"]);
        var darkBackground = Assert.IsType<SolidColorBrush>(dark["PageBackgroundBrush"]);
        var lightSurface = Assert.IsType<SolidColorBrush>(light["SurfaceBrush"]);
        var darkSurface = Assert.IsType<SolidColorBrush>(dark["SurfaceBrush"]);

        Assert.NotEqual(lightBackground.Color, darkBackground.Color);
        Assert.NotEqual(lightSurface.Color, darkSurface.Color);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("storage", "storage", true)]
    [InlineData("support", "support", true)]
    [InlineData("task", "support", false)]
    [InlineData("TASK", "task", true)]
    [InlineData(null, "task", false)]
    public void ActiveNavigationRoute_IsCaseInsensitiveAndSafe(string? current, string target, bool expected)
    {
        var converter = new RouteMatchConverter();
        var result = converter.Convert(new object[] { current ?? DependencyProperty.UnsetValue, target },
            typeof(bool), new object(), CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    private static ResourceDictionary Load(string name) =>
        new() { Source = new Uri($"/FRMS;component/Themes/{name}.xaml", UriKind.Relative) };
}
