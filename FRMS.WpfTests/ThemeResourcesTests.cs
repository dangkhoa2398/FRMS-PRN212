using System.Globalization;
using System.Xml.Linq;
using FRMS.PRN212.WpfConnection.Theming;
using Xunit;

namespace FRMS.WpfTests;

public sealed class ThemeResourcesTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void LightAndDarkThemes_HaveIdenticalSemanticKeysAndDistinctSurfaces()
    {
        var light = Palette("Light.xaml");
        var dark = Palette("Dark.xaml");
        Assert.True(light.Count >= 20);
        Assert.Equal(light.Keys.OrderBy(x => x), dark.Keys.OrderBy(x => x));
        Assert.NotEqual(light["PageBackgroundBrush"], dark["PageBackgroundBrush"]);
        Assert.NotEqual(light["SurfaceBrush"], dark["SurfaceBrush"]);
        Assert.All(light.Values, value => Assert.Matches("^#[0-9A-Fa-f]{6,8}$", value));
        Assert.All(dark.Values, value => Assert.Matches("^#[0-9A-Fa-f]{6,8}$", value));
    }

    [Fact]
    public void SharedStyles_DeclareButtonsCardsAndRouteConverter()
    {
        var doc = Asset("Controls.xaml");
        var keys = doc.Root!.Elements()
            .Select(x => (string?)x.Attribute(Xaml + "Key"))
            .Where(x => x is not null)
            .ToHashSet();
        Assert.Contains("AppButtonStyle", keys);
        Assert.Contains("SidebarButtonStyle", keys);
        Assert.Contains("GhostButtonStyle", keys);
        Assert.Contains("CardBorderStyle", keys);
        Assert.Contains("RouteMatchConverter", keys);
    }

    [Theory]
    [InlineData("storage", "storage", true)]
    [InlineData("support", "support", true)]
    [InlineData("task", "support", false)]
    [InlineData("TASK", "task", true)]
    [InlineData(null, "task", false)]
    public void ActiveNavigationRoute_IsCaseInsensitiveAndSafe(string? current, string target, bool expected)
    {
        var converter = new RouteMatchConverter();
        var result = converter.Convert(new object[] { current ?? System.Windows.DependencyProperty.UnsetValue, target },
            typeof(bool), new object(), CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    private static Dictionary<string, string> Palette(string fileName) =>
        Asset(fileName).Root!.Elements(Wpf + "SolidColorBrush").ToDictionary(
            element => (string)element.Attribute(Xaml + "Key")!,
            element => (string)element.Attribute("Color")!);

    private static XDocument Asset(string name) =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", name));
}
