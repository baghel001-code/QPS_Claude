namespace Admin.Components.Dashboard;

/// <summary>Everything a dummy dashboard shows. Real dashboards can replace these one by one (see HomeDashboards).</summary>
public sealed record DashboardDefinition(
    string Title,
    string Subtitle,
    IReadOnlyList<DashAction> Actions,
    IReadOnlyList<Kpi> Kpis,
    DashTable Table,
    DashSide Side,
    string? Badge = null);

/// <param name="Tone">pink, blue, green, amber or red.</param>
public sealed record Kpi(string Label, string Value, string? Note, string Tone, string Icon);

public sealed record DashAction(string Label, string Href, string Icon, bool Primary = true);

/// <summary>A table; if the last column is "Status" its cells are shown as status chips.</summary>
public sealed record DashTable(string Title, string? Link, string[] Columns, string[][] Rows);

public enum SideKind { Bars, List }

/// <summary>Right-hand panel: horizontal bars (one colour, values printed) or a short list.</summary>
public sealed record DashSide(string Title, string? Link, SideKind Kind, IReadOnlyList<SideItem> Items,
    string? Hint = null, double? Max = null, string Suffix = "");

public sealed record SideItem(string Label, string Detail = "", string? Status = null, double Value = 0);
