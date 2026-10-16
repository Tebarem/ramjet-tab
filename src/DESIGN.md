# TAB: design notes

The tab list and the nametags, per viewer. A plugin for Ramjet (a C++ Minecraft server with a C# plugin layer). Everything the plugin writes is
seen by one viewer only; the server's own list names, teams, scoreboard and list order are never touched.

## Layout

| Path | What |
|------|------|
| `src/Core/TabConfig.cs` | The settings model (the JSON shape) and its source-generated JSON context. |
| `src/Core/CompiledTab.cs` | Compiles a settings value once per load: sorted groups, sort rules, intervals, and the list of problems to report. |
| `src/Core/ConfigResolver.cs` | Merges the layers for one player: world, user, group, default (field by field). |
| `src/Core/TabSorter.cs` | Compiles the sort chain and orders players by it. Pure. |
| `src/Core/PlaceholderTemplate.cs` | Reads the placeholder tokens of a template (for the dump and for the tests). |
| `src/TabPlugin.cs` | The plugin: load, placeholders, refresh, apply to the presence layer. |
| `src/TabCommands.cs` | `/tab reload` and `/tab dump <player>`. |
| `tests/Tab.Tests/` | Console tests of `src/Core`. They reference no game API. |

`src/Core` uses only the base class library. The plugin files use `Mc.Scripting.Api` and run only inside the server.

## Settings

One file, `data/config.json`, read with the source-generated JSON context. Layers, highest first:

1. **world**: `worlds.<world key>` (for example `minecraft:the_nether`).
2. **user**: `users.<profile id>` (dashed uuid text, case insensitive).
3. **group**: the matching group of highest `weight`. A group matches when the player holds its `permission` node (default `tab.group.<name>`).
4. **default**: `groups._DEFAULT_`.

Each field takes the first layer that sets it, so the nametag prefix, the nametag suffix and the tab list texts resolve separately. A `null` field
means "not set here". Booleans (`invisible-nametags`, `enable-collision`) are the text `"true"` or `"false"`; anything else is reported and the default
is used (hidden: false; collision: true).

Problems found when the file is loaded are logged as `settings: ...` and replaced by defaults: a refresh below 1 tick, a placeholder interval that is
neither 0 nor at least 50 ms, an unreadable boolean, a sort rule with an unknown kind or without a value.

## Refresh and apply

Every `refresh-ticks` (default 20) the plugin runs one refresh on the tick thread:

1. Read the ping of every player from one players snapshot (one engine call per refresh).
2. Resolve each player's style and render the sort key of each sort rule.
3. Sort. The result is the list order of each player (`TabEntry.Order`).
4. Render the texts of each player (tab name, nametag prefix and suffix). The result is a small value record per target.
5. A target whose record changed since the last refresh is sent to every viewer. A viewer who has not yet received the full state (a new viewer,
   or a viewer after a settings load) gets every target. Everything else is skipped, so an idle server sends almost nothing.
6. The header and footer are rendered per viewer and sent every refresh; the presence layer drops an identical header.

Writes go through `plugin.Tablist` (name, order, header, footer) and `plugin.Nametags` (prefix, suffix, visibility, collision). The presence layer
keeps the sent value of every field and writes only what changed. The plugin registers nothing with the server's global teams or list name.

A settings load (on edit, or `/tab reload`) recompiles, re-registers the placeholders, re-registers the group permission nodes, restarts the
refresh at the new period, and clears the sent state so everything is sent again. Unloading the plugin releases its presence state and its
placeholders through its context.

## Sorting

The chain is a list of rules applied in order. The first rule that separates two players decides; the name (ignoring case, then ordinal) breaks
the rest. Kinds:

| Kind | Compares | Default direction |
|------|----------|-------------------|
| `group` | the weight of the resolved group | highest first |
| `alpha` | the rendered `value` (default `%tab_name%`) | ascending |
| `numeric` | the rendered `value` as a number, rounded to 5 decimals and clamped to ±1e9; text that is not a number sorts last | highest first |
| `ordered` | the position of the rendered `value` in `order`; unlisted texts sort after the list | ascending |

`descending` overrides the default. `ignore-case` applies to `alpha` and `ordered` (default true). The sort is an insertion sort, which is stable
and cheap for the few hundred entries a tab list holds.

## Placeholders

The engine's placeholder registry renders all text (`%id%`, `%id:arg%`, `%%` for a percent sign). This plugin registers these ids (the registry
accepts lowercase letters, digits and underscores only):

| Id | Scope | Value | Default cache |
|----|-------|-------|---------------|
| `tab_name` | player | the player's name | evaluated each time |
| `tab_world` | player | the world key | 1 s |
| `tab_gamemode` | player | survival, creative, adventure or spectator | 1 s |
| `tab_health` | player | health, rounded up | 1 s |
| `tab_food` | player | food level | 1 s |
| `tab_xp` | player | experience level | 1 s |
| `tab_ping` | player | latency in ms, from the last refresh's snapshot | 1 s |
| `tab_prefix` / `tab_suffix` | player | the permission meta prefix or suffix (any `IPlayerMeta` provider) | 1 s |
| `tab_group` | player | the resolved group name | 5 s |
| `tab_online` | global | players online | 1 s |
| `tab_tps` / `tab_mspt` | global | server tick rate and average tick time | 1 s |

The cache of each id can be changed with `placeholders.<id>.interval_ms`. The default nametag prefix and suffix are `%tab_prefix%` and
`%tab_suffix%`, so a permissions plugin that supplies meta shows up in the nametags without any extra setting.

## What is not built yet

- Conditions over placeholders (`%a% == b`), scoreboards with conditions, bossbars and layouts (fake tab entries): not started.
- Belowname and the yellow number in the list: the presence API exists (`plugin.Belowname`, `plugin.Tablist.SetObjective`), not wired.
- Ping spoofing (latency per viewer): the presence API has it; not wired to a setting.
- Viewer overrides by command (show or hide one nametag for one viewer), collision override command, scoreboard toggle: not built.
- Per-placeholder timing and error diagnostics: errors are logged by the engine; no timing command yet.
- Animated header and footer frames: the text is static.

## Deviations from the first plan

- Placeholder ids use underscores (`tab_online`), not colons: the registry only accepts lowercase letters, digits and underscores.
- The group is found by permission node (`tab.group.<name>`, or an explicit `permission`), not by reading the permission provider's group list. The
  public permission API has no group lookup. Group nodes are registered as default-false, so an operator grants them with the permissions plugin.
- Ties in the sort are broken by name only. The `|` fallback syntax is not implemented.
- Sorting depends on the client honouring the list order field of the tab list packet. That is not verified without a real client (see below).

## Real-client checks

These cannot run here; they need one client on a scratch server:

1. Sorting: the tab list shows players in the order the rules give. (If the client ignores the list order field, sorting falls back to name order.)
2. Nametags: a viewer sees the prefix and suffix of other players, and a player's own prefix for another viewer is not affected.
3. Visibility "hidden" hides the nametag for every viewer; collision "false" lets players pass through each other.
4. A player joining and leaving updates every viewer; a reconnect shows no stale team.
5. Header and footer per viewer; `/tab reload` resends everything.
6. Unloading the plugin removes the tab names, nametags, order and header.
