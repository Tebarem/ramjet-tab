namespace Tab.Core;

/// <summary>A permission group: its name, the node that grants it, its weight and its settings.</summary>
public sealed record GroupEntry(string Name, string Permission, int Weight, TabStyle Style);

/// <summary>The settings after one load: groups sorted by weight, the sort rules compiled, intervals checked. Problems are the things the admin should fix;
/// each one has been replaced by a default, so the compiled form is always usable. Built once per load and read on the tick thread.</summary>
public sealed class CompiledTab
{
    /// <summary>The group that applies when no other group matches.</summary>
    public const string DefaultGroup = "_DEFAULT_";

    /// <summary>The smallest refresh interval of a placeholder, in milliseconds (one tick).</summary>
    public const int MinIntervalMs = 50;

    private readonly Dictionary<string, int> _intervals = [];

    private CompiledTab(TabConfig source)
    {
        Source = source;
    }

    /// <summary>The settings as read from the file.</summary>
    public TabConfig Source { get; }

    /// <summary>The groups other than <c>_DEFAULT_</c>, highest weight first.</summary>
    public List<GroupEntry> Groups { get; } = [];

    /// <summary>The <c>_DEFAULT_</c> layer (an empty layer when the file has none).</summary>
    public TabStyle Defaults { get; private set; } = new();

    /// <summary>Per user layers, keyed by profile id text (case insensitive).</summary>
    public Dictionary<string, TabStyle> Users { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per world layers, keyed by world key.</summary>
    public Dictionary<string, TabStyle> Worlds { get; } = new(StringComparer.Ordinal);

    /// <summary>The compiled sort chain (empty when sorting is off).</summary>
    public List<SortRule> SortRules { get; } = [];

    /// <summary>Whether the list order is set by this plugin.</summary>
    public bool SortEnabled { get; private set; }

    /// <summary>Ticks between two refreshes.</summary>
    public int RefreshTicks { get; private set; } = 20;

    /// <summary>Things the admin should fix. Empty when the file is clean.</summary>
    public List<string> Problems { get; } = [];

    /// <summary>Compiles a settings value. Never throws for bad values; it reports them in <see cref="Problems"/> instead.</summary>
    public static CompiledTab Compile(TabConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var compiled = new CompiledTab(config);
        compiled.Build();
        return compiled;
    }

    /// <summary>The refresh interval of a placeholder in milliseconds (its config entry, else <paramref name="fallback"/>).</summary>
    public int IntervalMs(string id, int fallback) => _intervals.TryGetValue(id, out int ms) ? ms : fallback;

    private void Build()
    {
        if (Source.RefreshTicks >= 1) RefreshTicks = Source.RefreshTicks;
        else Problems.Add($"refresh-ticks {Source.RefreshTicks} is below 1; using {RefreshTicks}");

        foreach (var (name, style) in Source.Groups)
        {
            if (style is null)
            {
                Problems.Add($"groups.{name} is empty and is ignored");
                continue;
            }
            CheckStyle(style, "groups." + name);
            if (name == DefaultGroup)
            {
                Defaults = style;
                continue;
            }
            if (name.Length == 0)
            {
                Problems.Add("a group has an empty name and is ignored");
                continue;
            }
            Groups.Add(new GroupEntry(name, style.Permission ?? "tab.group." + name.ToLowerInvariant(), style.Weight ?? 0, style));
        }
        Groups.Sort((a, b) =>
        {
            int byWeight = b.Weight.CompareTo(a.Weight);
            return byWeight != 0 ? byWeight : string.CompareOrdinal(a.Name, b.Name);
        });

        foreach (var (uuid, style) in Source.Users)
        {
            if (style is null) continue;
            CheckStyle(style, "users." + uuid);
            Users[uuid] = style;
        }
        foreach (var (world, style) in Source.Worlds)
        {
            if (style is null) continue;
            CheckStyle(style, "worlds." + world);
            Worlds[world] = style;
        }

        foreach (var (id, placeholder) in Source.Placeholders)
        {
            if (placeholder is null) continue;
            int ms = placeholder.IntervalMs;
            if (ms != 0 && ms < MinIntervalMs)
            {
                Problems.Add($"placeholders.{id}.interval_ms {ms} is below {MinIntervalMs} (0 or at least {MinIntervalMs}); using 1000");
                ms = 1000;
            }
            _intervals[id] = ms;
        }

        SortEnabled = Source.Sorting.Enabled;
        if (SortEnabled) SortRules.AddRange(TabSorter.Compile(Source.Sorting.Rules, Problems));
    }

    private void CheckStyle(TabStyle style, string where)
    {
        CheckBool(style.InvisibleNametags, where + ".invisible-nametags");
        CheckBool(style.EnableCollision, where + ".enable-collision");
    }

    private void CheckBool(string? text, string where)
    {
        if (text is null || text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("false", StringComparison.OrdinalIgnoreCase)) return;
        Problems.Add($"{where} is \"{text}\", not true or false; the default is used");
    }
}
