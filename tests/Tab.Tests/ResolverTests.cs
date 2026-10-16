using Tab.Core;

namespace Tab.Tests;

/// <summary>The layer order of <see cref="ConfigResolver"/>: world, user, group, default, field by field.</summary>
internal static class ResolverTests
{
    private const string kUser = "0f0e0d0c-0b0a-0908-0706-050403020100";
    private const string kOverworld = "minecraft:overworld";
    private const string kNether = "minecraft:the_nether";

    private sealed class Perms(params string[] nodes) : IPermissionCheck
    {
        public bool Has(string node) => Array.IndexOf(nodes, node) >= 0;
    }

    private static CompiledTab Settings(Action<TabConfig> edit)
    {
        var config = new TabConfig();
        config.Groups.Clear();
        config.Groups["_DEFAULT_"] = new TabStyle
        {
            TagPrefix = "[def]",
            TagSuffix = "[def-suffix]",
            TabPrefix = "",
            TabSuffix = "",
            InvisibleNametags = "false",
            EnableCollision = "true",
        };
        edit(config);
        return CompiledTab.Compile(config);
    }

    public static void All()
    {
        Program.Run("resolver: nobody matches, the default applies", () =>
        {
            CompiledTab tab = Settings(c => c.Groups["admin"] = new TabStyle { Weight = 100, TagPrefix = "[adm]" });
            ResolvedStyle style = ConfigResolver.Resolve(tab, kUser, kOverworld, new Perms());
            Program.Check(style.Group == "_DEFAULT_" && style.Weight == 0, "group is the default");
            Program.Check(style.TagPrefix == "[def]" && style.TagSuffix == "[def-suffix]", "default texts");
            Program.Check(!style.InvisibleNametag && style.Collision, "default visibility and collision");
        });

        Program.Run("resolver: the heavier of two matching groups wins", () =>
        {
            CompiledTab tab = Settings(c =>
            {
                c.Groups["mod"] = new TabStyle { Weight = 50, TagPrefix = "[mod]" };
                c.Groups["admin"] = new TabStyle { Weight = 100, TagPrefix = "[adm]" };
            });
            ResolvedStyle both = ConfigResolver.Resolve(tab, kUser, kOverworld, new Perms("tab.group.admin", "tab.group.mod"));
            Program.Check(both.Group == "admin" && both.TagPrefix == "[adm]" && both.Weight == 100, "admin over mod");
            ResolvedStyle mod = ConfigResolver.Resolve(tab, kUser, kOverworld, new Perms("tab.group.mod"));
            Program.Check(mod.Group == "mod" && mod.TagPrefix == "[mod]", "mod alone");
        });

        Program.Run("resolver: an explicit permission node grants the group", () =>
        {
            CompiledTab tab = Settings(c => c.Groups["vip"] = new TabStyle { Permission = "shop.vip", TagPrefix = "[vip]" });
            Program.Check(ConfigResolver.Resolve(tab, kUser, kOverworld, new Perms("tab.group.vip")).Group == "_DEFAULT_",
                "the default node does not grant it");
            Program.Check(ConfigResolver.Resolve(tab, kUser, kOverworld, new Perms("shop.vip")).Group == "vip", "the explicit node does");
        });

        Program.Run("resolver: prefix and suffix resolve separately", () =>
        {
            CompiledTab tab = Settings(c =>
            {
                c.Groups["admin"] = new TabStyle { Weight = 1, TagPrefix = "[adm]" };
                c.Users[kUser] = new TabStyle { TagSuffix = "[user]" };
            });
            ResolvedStyle style = ConfigResolver.Resolve(tab, kUser, kOverworld, new Perms("tab.group.admin"));
            Program.Check(style.TagPrefix == "[adm]", "prefix from the group");
            Program.Check(style.TagSuffix == "[user]", "suffix from the user, not the default");
        });

        Program.Run("resolver: user beats group, world beats user", () =>
        {
            CompiledTab tab = Settings(c =>
            {
                c.Groups["admin"] = new TabStyle { Weight = 1, TagPrefix = "[group]" };
                c.Users[kUser] = new TabStyle { TagPrefix = "[user]" };
                c.Worlds[kNether] = new TabStyle { TagPrefix = "[world]" };
            });
            Perms admin = new("tab.group.admin");
            Program.Check(ConfigResolver.Resolve(tab, kUser, kOverworld, admin).TagPrefix == "[user]", "user over group");
            Program.Check(ConfigResolver.Resolve(tab, kUser, kNether, admin).TagPrefix == "[world]", "world over user");
            Program.Check(ConfigResolver.Resolve(tab, "other", kOverworld, admin).TagPrefix == "[group]", "another player gets the group");
        });

        Program.Run("resolver: user ids match in any case", () =>
        {
            CompiledTab tab = Settings(c => c.Users[kUser] = new TabStyle { TagPrefix = "[user]" });
            Program.Check(ConfigResolver.Resolve(tab, kUser.ToUpperInvariant(), kOverworld, new Perms()).TagPrefix == "[user]",
                "upper-case id finds the user");
        });

        Program.Run("resolver: nametag visibility and collision follow the layers", () =>
        {
            CompiledTab tab = Settings(c =>
            {
                c.Groups["ghost"] = new TabStyle { Weight = 5, InvisibleNametags = "true", EnableCollision = "false" };
                c.Users[kUser] = new TabStyle { InvisibleNametags = "false" };
            });
            Perms ghost = new("tab.group.ghost");
            ResolvedStyle style = ConfigResolver.Resolve(tab, "nobody", kOverworld, ghost);
            Program.Check(style.InvisibleNametag && !style.Collision, "group sets hidden and no collision");
            Program.Check(!ConfigResolver.Resolve(tab, kUser, kOverworld, ghost).InvisibleNametag, "user shows the nametag again");
            Program.Check(ConfigResolver.Resolve(tab, "nobody", kNether, ghost).InvisibleNametag, "world without the key keeps the group");
        });

        Program.Run("resolver: an unreadable boolean falls back to the default", () =>
        {
            CompiledTab tab = Settings(c => c.Groups["bad"] = new TabStyle { Weight = 1, InvisibleNametags = "yes", EnableCollision = "no" });
            ResolvedStyle style = ConfigResolver.Resolve(tab, "nobody", kOverworld, new Perms("tab.group.bad"));
            Program.Check(!style.InvisibleNametag, "invisible falls back to false");
            Program.Check(style.Collision, "collision falls back to true");
        });

        Program.Run("resolver: an empty default layer still resolves", () =>
        {
            var config = new TabConfig();
            config.Groups.Clear();
            ResolvedStyle style = ConfigResolver.Resolve(CompiledTab.Compile(config), "nobody", kOverworld, new Perms());
            Program.Check(style.TagPrefix == "" && style.TagSuffix == "" && style.Collision && !style.InvisibleNametag, "built-in fallbacks");
        });
    }
}
