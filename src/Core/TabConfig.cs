using System.Text.Json.Serialization;

namespace Tab.Core;

/// <summary>The settings file (<c>plugins/tab/data/config.json</c>). Keys reuse familiar TAB names where the meaning matches. Groups, users and worlds
/// are layered over <c>_DEFAULT_</c> by <see cref="ConfigResolver"/>; a null field in a layer means "not set here".</summary>
public sealed class TabConfig
{
    /// <summary>Lines of the header, joined with a line break. Placeholders are rendered per viewer.</summary>
    [JsonPropertyName("header")]
    public List<string> Header { get; set; } = ["&bRamjet", "%tab_online% online"];

    /// <summary>Lines of the footer, joined with a line break.</summary>
    [JsonPropertyName("footer")]
    public List<string> Footer { get; set; } = ["&7%tab_tps% TPS"];

    /// <summary>Ticks between two refreshes of names, nametags, order and header (20 = one second).</summary>
    [JsonPropertyName("refresh-ticks")]
    public int RefreshTicks { get; set; } = 20;

    /// <summary>Permission groups by name. A group applies to a player who has its permission (<c>permission</c>, default <c>tab.group.NAME</c>);
    /// the highest <c>weight</c> wins. <c>_DEFAULT_</c> applies to everybody else and must be complete.</summary>
    [JsonPropertyName("groups")]
    public Dictionary<string, TabStyle> Groups { get; set; } = new()
    {
        ["_DEFAULT_"] = new TabStyle
        {
            TagPrefix = "%tab_prefix%",
            TagSuffix = "%tab_suffix%",
            TabPrefix = "",
            TabSuffix = "",
            InvisibleNametags = "false",
            EnableCollision = "true",
        },
    };

    /// <summary>Per player overrides, by profile id (the dashed uuid text). Above the group, below the world.</summary>
    [JsonPropertyName("users")]
    public Dictionary<string, TabStyle> Users { get; set; } = [];

    /// <summary>Per world overrides, by world key (<c>minecraft:overworld</c>). Above everything else.</summary>
    [JsonPropertyName("worlds")]
    public Dictionary<string, TabStyle> Worlds { get; set; } = [];

    /// <summary>How the tab list is ordered for each viewer (the list order field of the vanilla packet).</summary>
    [JsonPropertyName("sorting")]
    public SortingConfig Sorting { get; set; } = new();

    /// <summary>Refresh interval per placeholder id, in milliseconds (0 evaluates every time; otherwise at least 50).</summary>
    [JsonPropertyName("placeholders")]
    public Dictionary<string, PlaceholderConfig> Placeholders { get; set; } = [];
}

/// <summary>One layer of settings. Every field is nullable so that a layer can set only what it changes. Booleans are written as text
/// ("true" / "false") to match the nametag keys of TAB; an unreadable value is reported and falls back to the default.</summary>
public sealed class TabStyle
{
    /// <summary>Groups only: the permission node that grants the group. Default <c>tab.group.NAME</c>.</summary>
    [JsonPropertyName("permission")]
    public string? Permission { get; set; }

    /// <summary>Groups only: the rank among matching groups. Higher wins; ties go to the name that sorts first.</summary>
    [JsonPropertyName("weight")]
    public int? Weight { get; set; }

    /// <summary>Text before the name in the nametag (a placeholder template; legacy <c>&amp;</c> colour codes allowed).</summary>
    [JsonPropertyName("tagprefix")]
    public string? TagPrefix { get; set; }

    /// <summary>Text after the name in the nametag.</summary>
    [JsonPropertyName("tagsuffix")]
    public string? TagSuffix { get; set; }

    /// <summary>Text before the name in the tab list.</summary>
    [JsonPropertyName("tabprefix")]
    public string? TabPrefix { get; set; }

    /// <summary>Text after the name in the tab list.</summary>
    [JsonPropertyName("tabsuffix")]
    public string? TabSuffix { get; set; }

    /// <summary>"true" hides the nametag from every viewer; "false" shows it.</summary>
    [JsonPropertyName("invisible-nametags")]
    public string? InvisibleNametags { get; set; }

    /// <summary>"true" lets players of the same nametag style collide; "false" lets them pass through each other.</summary>
    [JsonPropertyName("enable-collision")]
    public string? EnableCollision { get; set; }
}

/// <summary>The sort chain. Rules are applied in order; the first rule that tells two players apart decides, and the name breaks the rest.</summary>
public sealed class SortingConfig
{
    /// <summary>When false the list order is left to the server.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>The rules, first to last.</summary>
    [JsonPropertyName("rules")]
    public List<SortRuleConfig> Rules { get; set; } =
    [
        new SortRuleConfig { Kind = "group" },
        new SortRuleConfig { Kind = "alpha", Value = "%tab_name%" },
    ];
}

/// <summary>One sort rule. <c>kind</c> is <c>group</c> (weight of the resolved group), <c>alpha</c> (text, ascending), <c>numeric</c> (number of the
/// placeholder in <c>value</c>, within ±1e9 and 5 decimals, highest first) or <c>ordered</c> (position of the text in <c>order</c>).</summary>
public sealed class SortRuleConfig
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "alpha";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    /// <summary>Null takes the kind's default: highest first for group and numeric, ascending for alpha and ordered.</summary>
    [JsonPropertyName("descending")]
    public bool? Descending { get; set; }

    [JsonPropertyName("ignore-case")]
    public bool IgnoreCase { get; set; } = true;

    [JsonPropertyName("order")]
    public List<string> Order { get; set; } = [];
}

/// <summary>Options of one placeholder.</summary>
public sealed class PlaceholderConfig
{
    [JsonPropertyName("interval_ms")]
    public int IntervalMs { get; set; } = 1000;
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(TabConfig))]
public partial class TabJson : System.Text.Json.Serialization.JsonSerializerContext
{
}
