using System.Reflection;
using MudBlazor;

namespace Admin.Components.Layout;

/// <summary>Shared by AuthLayout and DashboardLayout so every page uses the same colours.</summary>
public static class QpsTheme
{
    // Taken from the Vishal Mega Mart logo; same values as the --qps-* variables in the CSS files.
    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#d4007c",           // deeper logo pink (white text 5.1:1)
            Secondary = "#0f6cb1",         // logo blue
            Tertiary = "#ed018b",          // logo pink
            AppbarBackground = "#ffffff",
            AppbarText = "#1d1a24",
            Background = "#f6f4f7",
            DrawerBackground = "#ffffff",
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "8px", DrawerWidthLeft = "264px" },
    };
}

public static class AppInfo
{
    /// <summary>"App. Ver.1.4.2": informational version without the "+&lt;commit&gt;" suffix the SDK appends.</summary>
    public static readonly string Version =
        "App. Ver." + ((typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "NA")
            .Split('+')[0]);
}
