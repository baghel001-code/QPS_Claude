using System.Security.Claims;
using Admin.Auth.Access;

namespace Admin.Navigation;

/// <summary>A menu link (Href set) or a group (Children set).</summary>
public sealed record MenuNode(string Title, string? Href = null, string? Icon = null, IReadOnlyList<MenuNode>? Children = null)
{
    public bool IsGroup => Children is { Count: > 0 };
}

/// <summary>
/// The left menu, from the auth.Menu table: an entry is shown when the user has its permission,
/// a group when it has at least one visible entry. A page that appears twice in a group (same
/// Href, e.g. "Received" and "Submitted") is shown once: the first one the user may see.
/// Hiding a link is only convenience: the page itself must still check access ([RequirePermission]).
/// </summary>
public sealed class AppMenu(AccessService access)
{
    private const int MaxDepth = 5;   // guards against a parent/child loop in the table

    public async Task<IReadOnlyList<MenuNode>> ForAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var rights = await access.GetAsync(user, ct);
        var items = await access.GetMenuAsync(ct);
        var byParent = items.ToLookup(i => i.ParentId);
        return Build(byParent, parentId: null, rights, depth: 0);
    }

    private static List<MenuNode> Build(ILookup<int?, MenuItem> byParent, int? parentId, UserAccess rights, int depth)
    {
        var nodes = new List<MenuNode>();
        if (depth >= MaxDepth)
            return nodes;

        var hrefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in byParent[parentId].Where(i => i.IsActive).OrderBy(i => i.SortOrder))
        {
            if (!rights.HasAll(item.Permission))
                continue;
            if (item.Href is null)
            {
                var children = Build(byParent, item.Id, rights, depth + 1);
                if (children.Count > 0)
                    nodes.Add(new MenuNode(item.Title, Icon: item.Icon, Children: children));
            }
            else if (hrefs.Add(item.Href))
            {
                nodes.Add(new MenuNode(item.Title, item.Href, item.Icon));
            }
        }
        return nodes;
    }
}
