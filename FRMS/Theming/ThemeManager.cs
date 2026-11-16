using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace FRMS.PRN212.WpfConnection.Theming;

/// <summary>
/// Swaps shared WPF palette dictionaries without rebuilding views or losing user input.
/// Light is the safe default; a chosen theme is restored on the next launch.
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    private static string PreferencePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FRMS-PRN212", "theme.txt");

    public static void Initialize()
    {
        var dark = false;
        try
        {
            dark = string.Equals(File.ReadAllText(PreferencePath).Trim(), "Dark",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Apply(dark);
    }

    public static void Toggle()
    {
        Apply(!IsDark);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
            File.WriteAllText(PreferencePath, IsDark ? "Dark" : "Light");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Apply(bool dark)
    {
        var app = Application.Current;
        if (app is not null)
        {
            var dictionaries = app.Resources.MergedDictionaries;
            var existing = dictionaries.FirstOrDefault(x =>
                x.Source?.OriginalString.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true ||
                x.Source?.OriginalString.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true);
            var assemblyName = typeof(ThemeManager).Assembly.GetName().Name;
            var palette = dark ? "Dark" : "Light";
            var replacement = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/{assemblyName};component/Themes/{palette}.xaml",
                    UriKind.Absolute)
            };
            if (existing is null)
                dictionaries.Insert(0, replacement);
            else
                dictionaries[dictionaries.IndexOf(existing)] = replacement;
        }
        IsDark = dark;
    }
}
