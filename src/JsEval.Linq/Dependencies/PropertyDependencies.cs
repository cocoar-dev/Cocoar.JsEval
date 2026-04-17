namespace Cocoar.JsEval.Linq.Dependencies;

/// <summary>
/// Result of walking an Expression tree for property accesses.
/// </summary>
/// <remarks>
/// Paths are dotted (e.g. <c>"Address.City"</c>). Each path's root is guaranteed
/// to be a property of the root parameter type <typeparamref name="T"/> of the
/// collected expression — *not* a property of a closure-captured constant.
/// </remarks>
public sealed class PropertyDependencies
{
    /// <summary>Dotted property paths accessed on the root parameter.</summary>
    public IReadOnlySet<string> Paths { get; }

    /// <summary>Top-level property names only (root-level segment of each path).</summary>
    public IReadOnlySet<string> TopLevel { get; }

    /// <summary><c>true</c> if the collector encountered dynamic / unanalyzable access — callers should treat this as invalidate-all.</summary>
    public bool Unsafe { get; }

    public PropertyDependencies(IReadOnlySet<string> paths, bool isUnsafe)
    {
        Paths = paths;
        TopLevel = paths.Select(p => p.Split('.', 2)[0]).ToHashSet();
        Unsafe = isUnsafe;
    }

    /// <summary>Checks whether a property path (or its ancestor) is in the dependency set.</summary>
    public bool DependsOn(string path)
    {
        if (Unsafe) return true;
        if (Paths.Contains(path)) return true;
        var seg = path.Split('.');
        for (var i = 1; i < seg.Length; i++)
            if (Paths.Contains(string.Join('.', seg, 0, i))) return true;
        return false;
    }

    public override string ToString() =>
        Unsafe ? "<unsafe: invalidate-all>" : "{ " + string.Join(", ", Paths.OrderBy(p => p)) + " }";
}
