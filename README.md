# CS2-Plugin-Toggle

**English** · [简体中文](README.zh-CN.md)

Unload or restore specified CounterStrikeSharp plugins by map name. Matching uses a `Server.MapName` substring (workshop maps are typically `workshop/<id>/<bsp>`), not the whole custom mode.

From 1.1.0 the config is split into three files. Blacklist and whitelist can be active at the same time. 1.2.0 adds an optional mode profile layer (`profiles.json`) on top of the map lists.

## Install

Put `PluginToggle.dll` (and `PluginToggle.pdb` / `PluginToggle.deps.json` in the same directory) in:

```text
addons/counterstrikesharp/plugins/PluginToggle/
```

Restart CS2, or on an empty server run `css_plugins reload PluginToggle`. Config directory:

```text
addons/counterstrikesharp/configs/plugins/PluginToggle/
  config.json
  blacklist.json
  whitelist.json
  profiles.json
```

On first start, if a legacy `PluginToggle.json` is still present, it is migrated into the blacklist and default `config.json` / `whitelist.json` are written. The old file is renamed to `PluginToggle.json.bak`.

The `configs/` directory in this repo is an example. It is not copied to the server automatically.

## Config

### `config.json`

```json
{
  "Mode": "both",
  "ReloadOnLeave": true,
  "ApplyDelaySeconds": 0.35
}
```

- `Mode`: `blacklist` / `whitelist` / `both` (default `both`)
- `ReloadOnLeave`: when leaving a blacklist-matched map, whether to load back plugins unloaded this session
- `ApplyDelaySeconds`: reserved. Apply actually happens in `OnServerPreWorldUpdate`, not after this many seconds

### `blacklist.json`

On a `MapTokens` hit, unload `Plugins`. When leaving a matched map and `ReloadOnLeave` is true, only those unloaded this session are loaded back.

Default: on Cabin In The Woods (`3171695956` / `cabin`), unload BotBuy, BotOrders, BotRandomizer, BotChat.

### `whitelist.json`

On a `MapTokens` hit, keep them (load back if previously unloaded). On a **miss**, unload them. These plugins are only allowed to run on matched maps.

Default: `ZombieCabinDefence`; `UnloadAs` must be `Zombie Cabin Defense`.

### Plugin fields

- `Name`: plugin directory name used by `css_plugins load`
- `UnloadAs`: ModuleName used by `css_plugins unload`; empty means the same as `Name` (BotBuy is `BotBuyPatch`)

A plugin listed in both the map blacklist and map whitelist is skipped and logged. The same applies inside one profile. Map lists and profile lists may overlap; unload wins.

NeverTouch (BotAI, BotAimImprover, BotController, CS2-Switch-Gamemode, AdminPlus, etc.) is ignored on the map layer even if listed. Native plugins cannot be unloaded. The profile layer may unload AdminPlus; other NeverTouch names still skip.

### `profiles.json`

Optional. Missing file is written as `{ "Profiles": {} }` (same as 1.1.0). Example `funmatch` profile is in this repo's `configs/` and is not copied automatically.

When the active profile name matches a key, that profile's blacklist is unloaded and its whitelist is loaded. When the name is empty or does not match, previously unloaded blacklist plugins are restored and whitelist plugins are unloaded.

The profile name is in memory only. It does not survive process restart. `css_plugintoggle reload` rereads files and keeps the current name.

Map rules and profile rules are computed into one desired set, then unload runs before load.

## Commands

Console `css_plugintoggle` (`@css/generic`) prints mode, current map, profile, and both match / held / tokens.

`css_plugintoggle reload` rereads the four files, keeps the profile name, and applies again for the current map.

`css_plugintoggle profile <name>` stores the profile name (`-` or empty clears it). It does not apply until the next map start or `reload`, so a gamemode plugin can set the name and `changelevel` in the same tick.
