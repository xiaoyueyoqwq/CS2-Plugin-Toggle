# CS2-Plugin-Toggle

[English](README.md) · **简体中文**

按地图名卸载或恢复指定 CounterStrikeSharp 插件。匹配的是 `Server.MapName` 子串（工坊图一般为 `workshop/<id>/<bsp>`），不是整个 custom 模式。

1.1.0 起配置拆成三个文件，黑名单和白名单可以同时生效。1.2.0 增加可选的模式层（`profiles.json`），叠在地图名单之上。

## 安装

把 `PluginToggle.dll`（及同目录 `PluginToggle.pdb` / `PluginToggle.deps.json`）放到：

```text
addons/counterstrikesharp/plugins/PluginToggle/
```

重启 CS2，或空服 `css_plugins reload PluginToggle`。配置目录：

```text
addons/counterstrikesharp/configs/plugins/PluginToggle/
  config.json
  blacklist.json
  whitelist.json
  profiles.json
```

首次启动若仍有旧的 `PluginToggle.json`，会把它迁成黑名单，并写出默认 `config.json` / `whitelist.json`，旧文件改名为 `PluginToggle.json.bak`。

仓库 `configs/` 下是示例，不会自动拷到服务器。

## 配置

### `config.json`

```json
{
  "Mode": "both",
  "ReloadOnLeave": true,
  "ApplyDelaySeconds": 0.35
}
```

- `Mode`：`blacklist` / `whitelist` / `both`（默认 `both`）
- `ReloadOnLeave`：黑名单离开匹配图时，是否 load 回本次卸过的插件
- `ApplyDelaySeconds`：保留字段。实际在 `OnServerPreWorldUpdate` 里应用，不按这个秒数等

### `blacklist.json`

命中 `MapTokens` 时卸载 `Plugins`。离开匹配图且 `ReloadOnLeave` 为 true 时，只 load 本次卸过的项。

默认：Cabin In The Woods（`3171695956` / `cabin`）上卸载 BotBuy、BotOrders、BotRandomizer、BotChat。

### `whitelist.json`

命中 `MapTokens` 时保留（若先前卸过则 load 回来）。**未命中**时卸载。这些插件只允许在匹配图上跑。

默认：`ZombieCabinDefence`，`UnloadAs` 必须是 `Zombie Cabin Defense`。

### 插件字段

- `Name`：`css_plugins load` 用的插件目录名
- `UnloadAs`：`css_plugins unload` 用的 ModuleName；空则与 `Name` 相同（BotBuy 是 `BotBuyPatch`）

同一插件写进地图黑名单和地图白名单会被跳过并打日志。同一个 profile 的两份清单同理。地图层和模式层允许重叠，要卸时优先卸。

NeverTouch（BotAI、BotAimImprover、BotController、CS2-Switch-Gamemode、AdminPlus 等）在地图层写了也无效。native 卸不掉。模式层允许卸 AdminPlus；其余 NeverTouch 仍然跳过。

### `profiles.json`

可选。缺文件时写成 `{ "Profiles": {} }`（等于 1.1.0）。仓库 `configs/` 里的 `funmatch` 示例不会自动拷到服务器。

当前模式名命中某个 key 时：卸该 profile 的黑名单，load 白名单。模式名为空或未命中时：装回本进程因该 profile 卸过的黑名单，卸白名单。

模式名只存在内存，进程重启后为空。`css_plugintoggle reload` 重读文件并保留当前模式名。

地图规则和模式规则合成一套目标，先 unload 再 load。

## 命令

控制台 `css_plugintoggle`（`@css/generic`）打印 mode、当前图、profile、两份 match / held / tokens。

`css_plugintoggle reload` 重读四个文件，保留模式名，按当前图再应用一次。

`css_plugintoggle profile <name>` 写入模式名（`-` 或空则清空）。此时不 apply，等下次切图或 `reload`，以便切图插件在同一 tick 里先改名再 `changelevel`。
