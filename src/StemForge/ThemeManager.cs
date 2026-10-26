using System.Windows;
using System.Windows.Media;

namespace StemForge;

public static class ThemeManager
{
    public static void Apply(string theme)
    {
        bool light = theme == "Light";
        var colors = new Dictionary<string, string>
        {
            ["AppBackground"] = light ? "#F7F8F7" : "#0B0C0A",
            ["Surface"] = light ? "#FFFFFF" : "#101410",
            ["InputBackground"] = light ? "#F1F4F1" : "#151916",
            ["ButtonBackground"] = light ? "#E6ECE7" : "#1B201D",
            ["Text"] = light ? "#17251C" : "#E7ECE8",
            ["Muted"] = light ? "#53665A" : "#A4B3A9",
            ["Line"] = light ? "#B6C5BA" : "#394B40",
            ["Accent"] = light ? "#007D40" : "#00E676",
            ["ProcessedWave"] = light ? "#AA4800" : "#FFB454",
            ["DrumWave"] = light ? "#006D9C" : "#55CAFF",
            ["ProcessedDrumWave"] = light ? "#7535B5" : "#D296FF",
            ["kickWave"] = light ? "#006D9C" : "#55CAFF",
            ["snareWave"] = light ? "#B12F38" : "#FF8F8F",
            ["tomsWave"] = light ? "#7535B5" : "#C6A0FF",
            ["hatWave"] = light ? "#886100" : "#FFD166",
            ["rideWave"] = light ? "#127252" : "#76D9AF",
            ["crashWave"] = light ? "#A12B76" : "#FF9BD4",
            ["AccentText"] = light ? "#FFFFFF" : "#0B0C0A"
        };
        foreach (var (key, color) in colors)
            Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }
}
