using System.Globalization;
using System.Text;
using Tab.Core;

namespace Tab;

/// <summary>TAB: the tab list and the nametags, per viewer. Every refresh (<c>refresh-ticks</c>) it resolves each player's style from the settings, sorts
/// the list, renders the texts and writes what changed through the presence layer. Only the viewer sees the values; the server's own names, teams and
/// list order are left alone. Settings reload on edit (and with <c>/tab reload</c>).</summary>
[PluginInfo("TAB", Version = "0.1.0", Author = "Ramjet", Description = "Tab list header and footer, per-viewer names, sorting, nametag prefixes and suffixes, placeholders")]
public sealed partial class TabPlugin : Plugin
{
    private Config<TabConfig> _config = null!;
    private CompiledTab _tab = CompiledTab.Compile(new TabConfig());
    private ScheduledTask? _refresh;

    // the handles of the placeholders this plugin registered (replaced on each settings load)
    private readonly List<IPluginResource> _placeholders = [];

    // the last values written for each target; a viewer who has none (or a target whose values changed) gets them on the next refresh
    private readonly Dictionary<ulong, TargetState> _applied = [];
    private readonly HashSet<ulong> _seen = [];

    // read by the placeholders and the dump command, written by the refresh
    private readonly Dictionary<ulong, int> _pings = [];
    private readonly Dictionary<ulong, int> _positions = [];

    private sealed record TargetState(string TabPrefix, string TabSuffix, string TagPrefix, string TagSuffix, bool Hidden, bool Collision, int? Order);

    private sealed class PlayerPermissions(Player player) : IPermissionCheck
    {
        public bool Has(string node) => player.HasPermission(node);
    }

    private IPlaceholders Registry => Services.Get<IPlaceholders>();

    /// <summary>The settings in force (read by the commands).</summary>
    internal CompiledTab Settings => _tab;

    protected override void OnEnable()
    {
        _config = Config.Load(TabJson.Default.TabConfig);
        _config.Changed += _ => Scheduler.Post(ApplySettings);
        _config.Watch = true;

        Permissions.Register("tab.admin", PermissionDefault.OpLevel(3), "Reload the tab list settings and dump a player's tab state");
        TabCommands.Register(this);
        ApplySettings();
    }

    protected override void OnDisable()
    {
        _refresh?.Cancel();
        _refresh = null;
    }

    /// <summary>Reads the settings file now and installs it. Returns false when the file cannot be read; the settings in force then stay.</summary>
    internal bool ReloadNow()
    {
        bool read = _config.Reload();
        ApplySettings();
        return read;
    }

    /// <summary>The state of one player as the refresh sees it, for <c>/tab dump</c>.</summary>
    internal List<string> Describe(Player target)
    {
        CompiledTab tab = _tab;
        ResolvedStyle style = ConfigResolver.Resolve(tab, target.Uuid.ToString(), target.World.Key, new PlayerPermissions(target));
        var lines = new List<string>
        {
            $"group {style.Group} (weight {style.Weight}), world {target.World.Key}",
            $"tab name: {Render(target, style.TabPrefix)}{target.Name}{Render(target, style.TabSuffix)}",
            $"nametag: prefix \"{Render(target, style.TagPrefix)}\" suffix \"{Render(target, style.TagSuffix)}\" hidden {style.InvisibleNametag}, collision {style.Collision}",
            _positions.TryGetValue(target.Entity.Id, out int rank) ? $"list position {rank + 1} of {_positions.Count}" : "list position: server order (sorting is off)",
        };

        string[] templates = [style.TagPrefix, style.TagSuffix, style.TabPrefix, style.TabSuffix, string.Join(" ", tab.Source.Header)];
        var ids = new List<string>();
        foreach (string template in templates)
            foreach (string id in PlaceholderTemplate.Ids(template))
                if (!ids.Contains(id)) ids.Add(id);
        foreach (string id in ids) lines.Add($"%{id}% = {Registry.Resolve(target, "%" + id + "%")}");
        return lines;
    }

    /// <summary>Installs the settings in <see cref="_config"/>: compiles them, registers the placeholders and the group permissions, and restarts the
    /// refresh at the new period. Everything is sent again on the next refresh.</summary>
    private void ApplySettings()
    {
        _tab = CompiledTab.Compile(_config.Value);
        foreach (string problem in _tab.Problems) Log.Warn("settings: " + problem);

        RegisterPlaceholders();
        foreach (GroupEntry group in _tab.Groups)
            Permissions.Register(group.Permission, PermissionDefault.False, "TAB group " + group.Name);

        _refresh?.Cancel();
        _refresh = Scheduler.RunEvery(_tab.RefreshTicks, Refresh, initialDelayTicks: 1);
        _applied.Clear();
        _seen.Clear();
    }

    private void RegisterPlaceholders()
    {
        foreach (IPluginResource old in _placeholders) old.Dispose();
        _placeholders.Clear();

        IPlaceholders registry = Registry;
        Add(registry.Register("tab_name", PlaceholderScope.Player, Interval("tab_name", 0), (p, _) => p.Name));
        Add(registry.Register("tab_world", PlaceholderScope.Player, Interval("tab_world", 1000), (p, _) => p.World.Key));
        Add(registry.Register("tab_gamemode", PlaceholderScope.Player, Interval("tab_gamemode", 1000),
            (p, _) => p.GameMode.ToString().ToLowerInvariant()));
        Add(registry.Register("tab_health", PlaceholderScope.Player, Interval("tab_health", 1000),
            (p, _) => Math.Ceiling(p.Health).ToString(CultureInfo.InvariantCulture)));
        Add(registry.Register("tab_food", PlaceholderScope.Player, Interval("tab_food", 1000),
            (p, _) => p.FoodLevel.ToString(CultureInfo.InvariantCulture)));
        Add(registry.Register("tab_xp", PlaceholderScope.Player, Interval("tab_xp", 1000),
            (p, _) => p.ExperienceLevel.ToString(CultureInfo.InvariantCulture)));
        Add(registry.Register("tab_ping", PlaceholderScope.Player, Interval("tab_ping", 1000),
            (p, _) => _pings.TryGetValue(p.Entity.Id, out int ms) ? ms.ToString(CultureInfo.InvariantCulture) : "0"));
        Add(registry.Register("tab_online", PlaceholderScope.Global, Interval("tab_online", 1000),
            (_, _) => Server.PlayerCount.ToString(CultureInfo.InvariantCulture)));
        Add(registry.Register("tab_tps", PlaceholderScope.Global, Interval("tab_tps", 1000),
            (_, _) => Server.TickTimes().Tps.ToString("0.0", CultureInfo.InvariantCulture)));
        Add(registry.Register("tab_mspt", PlaceholderScope.Global, Interval("tab_mspt", 1000),
            (_, _) => Server.TickTimes().AverageMs.ToString("0.00", CultureInfo.InvariantCulture)));
        Add(registry.Register("tab_prefix", PlaceholderScope.Player, Interval("tab_prefix", 1000),
            (p, _) => Services.TryGet<IPlayerMeta>()?.AffixText(p.Uuid, AffixKind.Prefix) ?? ""));
        Add(registry.Register("tab_suffix", PlaceholderScope.Player, Interval("tab_suffix", 1000),
            (p, _) => Services.TryGet<IPlayerMeta>()?.AffixText(p.Uuid, AffixKind.Suffix) ?? ""));
        Add(registry.Register("tab_group", PlaceholderScope.Player, Interval("tab_group", 5000),
            (p, _) => ConfigResolver.Resolve(_tab, p.Uuid.ToString(), p.World.Key, new PlayerPermissions(p)).Group));
    }

    private TimeSpan Interval(string id, int fallbackMs) => TimeSpan.FromMilliseconds(_tab.IntervalMs(id, fallbackMs));

    private void Add(IPluginResource registration)
    {
        _placeholders.Add(registration);
        Context.Track(registration);
    }

    /// <summary>One refresh: resolve every player's style, sort, and write what changed to each viewer. Runs on the tick thread.</summary>
    private void Refresh()
    {
        IReadOnlyList<Player> online = Server.Players;
        if (online.Count == 0)
        {
            _applied.Clear();
            _seen.Clear();
            _pings.Clear();
            _positions.Clear();
            return;
        }
        RefreshPings();

        CompiledTab tab = _tab;
        int count = online.Count;
        var styles = new ResolvedStyle[count];
        var entries = new SortEntry[count];
        var present = new HashSet<ulong>(count);
        for (int i = 0; i < count; i++)
        {
            Player player = online[i];
            present.Add(player.Entity.Id);
            styles[i] = ConfigResolver.Resolve(tab, player.Uuid.ToString(), player.World.Key, new PlayerPermissions(player));
            entries[i] = new SortEntry(player.Name, styles[i].Weight, SortKeys(tab, player));
        }

        _positions.Clear();
        if (tab.SortEnabled)
        {
            int[] order = TabSorter.Order(tab.SortRules, entries);
            for (int rank = 0; rank < count; rank++) _positions[online[order[rank]].Entity.Id] = rank;
        }

        string header = RenderLines(tab.Source.Header);
        string footer = RenderLines(tab.Source.Footer);
        for (int j = 0; j < count; j++)
        {
            Player viewer = online[j];
            ApplyHeader(viewer, header, footer);
        }

        for (int i = 0; i < count; i++)
        {
            Player target = online[i];
            ResolvedStyle style = styles[i];
            var state = new TargetState(
                TabPrefix: Render(target, style.TabPrefix),
                TabSuffix: Render(target, style.TabSuffix),
                TagPrefix: Render(target, style.TagPrefix),
                TagSuffix: Render(target, style.TagSuffix),
                Hidden: style.InvisibleNametag,
                Collision: style.Collision,
                Order: _positions.TryGetValue(target.Entity.Id, out int rank) ? rank : null);

            bool changed = !_applied.TryGetValue(target.Entity.Id, out TargetState? previous) || previous != state;
            if (changed) _applied[target.Entity.Id] = state;
            for (int j = 0; j < count; j++)
            {
                Player viewer = online[j];
                if (changed || !_seen.Contains(viewer.Entity.Id)) ApplyTarget(viewer, target, state);
            }
        }

        // the viewers that are present have all their values now; the ones that left are forgotten
        for (int j = 0; j < count; j++) _seen.Add(online[j].Entity.Id);
        _seen.IntersectWith(present);
        RemoveAbsent(_applied, present);
        RemoveAbsent(_pings, present);
    }

    private void RefreshPings()
    {
        _pings.Clear();
        foreach (PlayerSnapshot snapshot in Server.PlayersSnapshot()) _pings[snapshot.EntityId] = snapshot.Ping;
    }

    // one key per rule that sorts by text or number: the rendered placeholder of that rule. A group rule has no key.
    private string[] SortKeys(CompiledTab tab, Player player)
    {
        var keys = new string[tab.SortRules.Count];
        for (int i = 0; i < keys.Length; i++)
        {
            SortRule rule = tab.SortRules[i];
            keys[i] = rule.Kind == SortKind.Group ? "" : Registry.Resolve(player, rule.Value);
        }
        return keys;
    }

    private void ApplyTarget(Player viewer, Player target, TargetState state)
    {
        Component display = Component.Join(
            Legacy(state.TabPrefix), Component.Text(target.Name), Legacy(state.TabSuffix));
        this.Tablist.Set(viewer, target, new TabEntry { Display = display, Order = state.Order });

        if (target.Name.Length == 0) return;
        this.Nametags.Set(viewer, target, new NametagStyle
        {
            Prefix = ComponentOrNull(state.TagPrefix),
            Suffix = ComponentOrNull(state.TagSuffix),
            Visibility = state.Hidden ? NametagVisibility.Never : NametagVisibility.Always,
            Collision = state.Collision ? NametagCollision.Always : NametagCollision.Never,
        });
    }

    private void ApplyHeader(Player viewer, string header, string footer)
    {
        this.Tablist.Header(viewer, ComponentOrNull(header), ComponentOrNull(footer));
    }

    private string RenderLines(List<string> lines)
    {
        var text = new StringBuilder();
        foreach (string line in lines)
        {
            if (text.Length > 0) text.Append('\n');
            text.Append(line);
        }
        return text.ToString();
    }

    private string Render(Player subject, string template) => template.Length == 0 ? "" : Registry.Resolve(subject, template);

    private static Component Legacy(string text) => Component.Legacy(text, '&');

    private static Component? ComponentOrNull(string text) => text.Length == 0 ? null : Component.Legacy(text, '&');

    private static void RemoveAbsent<T>(Dictionary<ulong, T> map, HashSet<ulong> present)
    {
        List<ulong>? gone = null;
        foreach (ulong id in map.Keys)
        {
            if (present.Contains(id)) continue;
            gone ??= [];
            gone.Add(id);
        }
        if (gone is null) return;
        foreach (ulong id in gone) map.Remove(id);
    }
}
