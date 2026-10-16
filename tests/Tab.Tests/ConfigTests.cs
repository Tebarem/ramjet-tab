using System.Text.Json;
using Tab.Core;

namespace Tab.Tests;

/// <summary>Parsing of the settings file and the checks the compiler makes on it.</summary>
internal static class ConfigTests
{
    private const string kSample = """
        {
          "header": ["Server", "%tab_online% online"],
          "footer": ["bye"],
          "refresh-ticks": 10,
          "groups": {
            "_DEFAULT_": { "tagprefix": "%tab_prefix%", "tagsuffix": "", "invisible-nametags": "false", "enable-collision": "true" },
            "admin": { "weight": 100, "tagprefix": "&c[Admin] " },
            "mod": { "weight": 50, "permission": "tab.custom.mod", "tagprefix": "&9[Mod] " }
          },
          "users": { "0f0e0d0c-0b0a-0908-0706-050403020100": { "tagsuffix": " <3" } },
          "worlds": { "minecraft:the_nether": { "invisible-nametags": "true" } },
          "sorting": { "enabled": true, "rules": [ { "kind": "group" }, { "kind": "numeric", "value": "%tab_ping%" } ] },
          "placeholders": { "tab_ping": { "interval_ms": 500 }, "tab_tps": { "interval_ms": 10 } }
        }
        """;

    public static void All()
    {
        Program.Run("config: sample parses into the model", () =>
        {
            TabConfig config = JsonSerializer.Deserialize(kSample, TabJson.Default.TabConfig)!;
            Program.Check(config.Header.Count == 2 && config.Header[1] == "%tab_online% online", "header lines");
            Program.Check(config.RefreshTicks == 10, "refresh-ticks");
            Program.Check(config.Groups["admin"].Weight == 100 && config.Groups["admin"].TagPrefix == "&c[Admin] ", "admin group");
            Program.Check(config.Groups["mod"].Permission == "tab.custom.mod", "explicit permission");
            Program.Check(config.Worlds["minecraft:the_nether"].InvisibleNametags == "true", "world layer");
            Program.Check(config.Sorting.Rules.Count == 2 && config.Sorting.Rules[1].Kind == "numeric", "sorting rules");
            Program.Check(config.Placeholders["tab_ping"].IntervalMs == 500, "placeholder interval");
        });

        Program.Run("config: default settings compile clean", () =>
        {
            CompiledTab tab = CompiledTab.Compile(new TabConfig());
            Program.Check(tab.Problems.Count == 0, "no problems for the defaults: " + string.Join("; ", tab.Problems));
            Program.Check(tab.Defaults.TagPrefix == "%tab_prefix%", "default nametag prefix comes from the permissions meta");
            Program.Check(tab.SortEnabled && tab.SortRules.Count == 2, "default sort chain is group then name");
        });

        Program.Run("config: default settings survive a round trip", () =>
        {
            string json = JsonSerializer.Serialize(new TabConfig(), TabJson.Default.TabConfig);
            TabConfig back = JsonSerializer.Deserialize(json, TabJson.Default.TabConfig)!;
            Program.Check(back.Groups.ContainsKey("_DEFAULT_"), "the default group is written");
            Program.Check(back.Footer.Count == 1, "footer is written");
        });

        Program.Run("config: malformed JSON is a JsonException", () =>
        {
            bool threw = false;
            try
            {
                JsonSerializer.Deserialize("{ \"groups\": [", TabJson.Default.TabConfig);
            }
            catch (JsonException)
            {
                threw = true;
            }
            Program.Check(threw, "malformed JSON throws, so the engine keeps the previous settings");
        });

        Program.Run("config: refresh below one tick is reported and replaced", () =>
        {
            CompiledTab tab = CompiledTab.Compile(new TabConfig { RefreshTicks = 0 });
            Program.Check(tab.RefreshTicks == 20, "refresh falls back to 20");
            Program.Check(tab.Problems.Count == 1 && tab.Problems[0].Contains("refresh-ticks"), "refresh problem reported");
        });

        Program.Run("config: a non-boolean nametag key is reported", () =>
        {
            var config = new TabConfig();
            config.Groups["admin"] = new TabStyle { InvisibleNametags = "maybe", EnableCollision = "TRUE" };
            CompiledTab tab = CompiledTab.Compile(config);
            Program.Check(tab.Problems.Count == 1 && tab.Problems[0].Contains("groups.admin.invisible-nametags"), "maybe is reported");
            Program.Check(tab.Problems.Count == 1, "TRUE (any case) is accepted");
        });

        Program.Run("config: placeholder intervals are 0 or at least one tick", () =>
        {
            var config = new TabConfig();
            config.Placeholders["tab_tps"] = new PlaceholderConfig { IntervalMs = 10 };
            config.Placeholders["tab_ping"] = new PlaceholderConfig { IntervalMs = 500 };
            config.Placeholders["tab_zero"] = new PlaceholderConfig { IntervalMs = 0 };
            CompiledTab tab = CompiledTab.Compile(config);
            Program.Check(tab.Problems.Count == 1 && tab.Problems[0].Contains("tab_tps"), "10 ms is reported");
            Program.Check(tab.IntervalMs("tab_tps", 1000) == 1000, "a bad interval falls back");
            Program.Check(tab.IntervalMs("tab_ping", 1000) == 500, "a good interval is kept");
            Program.Check(tab.IntervalMs("tab_zero", 1000) == 0, "zero means every time");
            Program.Check(tab.IntervalMs("tab_other", 1234) == 1234, "unlisted placeholders use the caller's default");
        });

        Program.Run("config: unknown and incomplete sort rules are reported and skipped", () =>
        {
            var config = new TabConfig();
            config.Sorting.Rules =
            [
                new SortRuleConfig { Kind = "weather" },
                new SortRuleConfig { Kind = "numeric", Value = "" },
                new SortRuleConfig { Kind = "ordered", Value = "%tab_group%", Order = ["owner", "admin"] },
            ];
            CompiledTab tab = CompiledTab.Compile(config);
            Program.Check(tab.Problems.Count == 2, "two problems: " + string.Join("; ", tab.Problems));
            Program.Check(tab.SortRules.Count == 1 && tab.SortRules[0].Kind == SortKind.Ordered, "only the ordered rule is kept");
        });

        Program.Run("config: sorting off leaves no rules", () =>
        {
            var config = new TabConfig();
            config.Sorting.Enabled = false;
            CompiledTab tab = CompiledTab.Compile(config);
            Program.Check(!tab.SortEnabled && tab.SortRules.Count == 0, "disabled sorting has no rules");
        });

        Program.Run("config: groups are highest weight first, then by name", () =>
        {
            var config = new TabConfig();
            config.Groups["zeta"] = new TabStyle { Weight = 10 };
            config.Groups["alpha"] = new TabStyle { Weight = 10 };
            config.Groups["top"] = new TabStyle { Weight = 99 };
            CompiledTab tab = CompiledTab.Compile(config);
            var names = tab.Groups.ConvertAll(g => g.Name);
            Program.Check(names.Count == 3 && names[0] == "top" && names[1] == "alpha" && names[2] == "zeta", "order: " + string.Join(",", names));
            Program.Check(tab.Groups[0].Permission == "tab.group.top", "default permission is tab.group.NAME");
        });
    }
}
