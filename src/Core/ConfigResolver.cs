namespace Tab.Core;

/// <summary>Whether a player holds a permission node. Implemented over the game's permission check at run time; a fake in tests.</summary>
public interface IPermissionCheck
{
    /// <summary>Whether the player holds the node.</summary>
    bool Has(string node);
}

/// <summary>The settings that apply to one player in one world, after all layers are merged. Texts are placeholder templates, rendered per player.</summary>
/// <param name="Group">The name of the group that applied (<c>_DEFAULT_</c> when none matched).</param>
/// <param name="Weight">The weight of that group (0 for the default).</param>
/// <param name="TagPrefix">Nametag text before the name.</param>
/// <param name="TagSuffix">Nametag text after the name.</param>
/// <param name="TabPrefix">Tab list text before the name.</param>
/// <param name="TabSuffix">Tab list text after the name.</param>
/// <param name="InvisibleNametag">The nametag is hidden from every viewer.</param>
/// <param name="Collision">Players of this style collide with each other.</param>
public sealed record ResolvedStyle(string Group, int Weight, string TagPrefix, string TagSuffix, string TabPrefix, string TabSuffix,
    bool InvisibleNametag, bool Collision);

/// <summary>Merges the settings layers for one player. Order, highest first: world, user, group, <c>_DEFAULT_</c>. Each field takes the first layer
/// that sets it, so prefix and suffix (and every other field) resolve separately. The group is the matching group of highest weight.</summary>
public static class ConfigResolver
{
    /// <summary>Resolves the style of a player.</summary>
    /// <param name="tab">The compiled settings.</param>
    /// <param name="uuid">The profile id text (dashed form), for the user layer.</param>
    /// <param name="world">The world key, for the world layer.</param>
    /// <param name="permissions">The player's permissions, for the group.</param>
    public static ResolvedStyle Resolve(CompiledTab tab, string uuid, string world, IPermissionCheck permissions)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(permissions);

        GroupEntry? group = null;
        foreach (GroupEntry candidate in tab.Groups)
        {
            if (!permissions.Has(candidate.Permission)) continue;
            group = candidate;
            break;
        }
        tab.Users.TryGetValue(uuid, out TabStyle? user);
        tab.Worlds.TryGetValue(world, out TabStyle? worldStyle);
        TabStyle? groupStyle = group?.Style;
        TabStyle defaults = tab.Defaults;

        return new ResolvedStyle(
            Group: group?.Name ?? CompiledTab.DefaultGroup,
            Weight: group?.Weight ?? 0,
            TagPrefix: Pick(worldStyle?.TagPrefix, user?.TagPrefix, groupStyle?.TagPrefix, defaults.TagPrefix) ?? "",
            TagSuffix: Pick(worldStyle?.TagSuffix, user?.TagSuffix, groupStyle?.TagSuffix, defaults.TagSuffix) ?? "",
            TabPrefix: Pick(worldStyle?.TabPrefix, user?.TabPrefix, groupStyle?.TabPrefix, defaults.TabPrefix) ?? "",
            TabSuffix: Pick(worldStyle?.TabSuffix, user?.TabSuffix, groupStyle?.TabSuffix, defaults.TabSuffix) ?? "",
            InvisibleNametag: ParseBool(Pick(worldStyle?.InvisibleNametags, user?.InvisibleNametags, groupStyle?.InvisibleNametags,
                defaults.InvisibleNametags), fallback: false),
            Collision: ParseBool(Pick(worldStyle?.EnableCollision, user?.EnableCollision, groupStyle?.EnableCollision, defaults.EnableCollision),
                fallback: true));
    }

    /// <summary>Reads "true" or "false" (any case). Anything else, or null, gives the fallback.</summary>
    public static bool ParseBool(string? text, bool fallback)
    {
        if (text is null) return fallback;
        if (text.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        return fallback;
    }

    private static string? Pick(string? first, string? second, string? third, string? fourth)
        => first ?? second ?? third ?? fourth;
}
