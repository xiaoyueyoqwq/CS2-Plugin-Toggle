# CS2-Plugin-Toggle

**English** · [简体中文](README.zh-CN.md)

Unload or restore specified CounterStrikeSharp plugins by map name. Matching uses a `Server.MapName` substring (workshop maps are typically `workshop/<id>/<bsp>`), not the whole custom mode.

From 1.1.0 the config is split into three files. Blacklist and whitelist can be active at the same time.

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

A plugin listed in both files is skipped and logged. NeverTouch (BotAI, BotAimImprover, BotController, CS2-Switch-Gamemode, etc.) is ignored even if listed. Native plugins cannot be unloaded.

## Commands

Console `css_plugintoggle` (`@css/generic`) prints mode, current map, and both match / held / tokens.

`css_plugintoggle reload` rereads the three files and applies again for the current map.
