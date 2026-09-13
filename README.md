# CS2-Plugin-Toggle

按地图名卸载或恢复指定 CounterStrikeSharp 插件。匹配的是 `Server.MapName` 子串（工坊图一般为 `workshop/<id>/<bsp>`），不是整个 custom 模式。

1.1.0 起配置拆成三个文件，黑名单和白名单可以同时生效。

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

同一插件写进两份清单会被跳过并打日志。NeverTouch（BotAI、BotAimImprover、BotController、CS2-Switch-Gamemode 等）写了也无效。native 卸不掉。

## 命令

控制台 `css_plugintoggle`（`@css/generic`）打印 mode、当前图、两份 match / held / tokens。

`css_plugintoggle reload` 重读三个文件并按当前图再应用一次。
