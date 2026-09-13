using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace PluginToggle;

[MinimumApiVersion(260)]
public sealed class PluginTogglePlugin : BasePlugin
{
    public override string ModuleName => "PluginToggle";
    public override string ModuleDescription => "Unload or load CSS plugins by map using blacklist and whitelist files";
    public override string ModuleAuthor => "xiaoyueyoqwq";
    public override string ModuleVersion => "1.1.0";

    private const string ConfigFileName = "config.json";
    private const string BlacklistFileName = "blacklist.json";
    private const string WhitelistFileName = "whitelist.json";
    private const string LegacyFileName = "PluginToggle.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    private static readonly HashSet<string> NeverTouch = new(StringComparer.OrdinalIgnoreCase)
    {
        "PluginToggle",
        "CS2-Plugin-Toggle",
        "BotAI",
        "Patches - Bot AI",
        "BotAimImprover",
        "BotController",
        "BotControllerImpl",
        "RayTrace",
        "RayTraceImpl",
        "CS2-Switch-Gamemode",
        "[CS2-Switch-Gamemode]",
        "AdminPlus",
        "AstraSkins",
        "CounterStrikeSharp",
    };

    private PluginToggleSettings _settings = PluginToggleSettings.Default();
    private PluginToggleList _blacklist = PluginToggleList.DefaultBlacklist();
    private PluginToggleList _whitelist = PluginToggleList.DefaultWhitelist();
    private readonly HashSet<string> _conflicts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blacklistHeld = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _whitelistHeld = new(StringComparer.OrdinalIgnoreCase);
    private string _lastMap = "";
    private bool _blacklistMatch;
    private bool _whitelistMatch;
    private bool _worldApplyArmed;
    private string? _pendingMap;

    private string ConfigDir => Path.Combine(
        Server.GameDirectory,
        "csgo",
        "addons",
        "counterstrikesharp",
        "configs",
        "plugins",
        "PluginToggle");

    public override void Load(bool hotReload)
    {
        AddCommand("css_plugintoggle", "Show PluginToggle state or reload config files", OnToggleCommand);

        if (!TryLoadFiles(out var error))
            Logger.LogError("Failed to load config files: {Error}", error);

        RegisterListener<Listeners.OnMapStart>(OnMapStart);

        Logger.LogInformation(
            "PluginToggle {Version} loaded. mode={Mode} blacklist=[{BlTokens}/{BlPlugins}] whitelist=[{WlTokens}/{WlPlugins}] reloadOnLeave={Reload}",
            ModuleVersion,
            _settings.Mode,
            string.Join(",", _blacklist.MapTokens),
            string.Join(",", _blacklist.Plugins.Select(p => p.Name)),
            string.Join(",", _whitelist.MapTokens),
            string.Join(",", _whitelist.Plugins.Select(p => p.Name)),
            _settings.ReloadOnLeave);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        Logger.LogInformation(
            "OnAllPluginsLoaded hotReload={Hot} map='{Map}'",
            hotReload,
            Server.MapName ?? "");
        ArmApply(Server.MapName);
    }

    public override void Unload(bool hotReload)
    {
        DisarmWorldApply();
        RemoveListener<Listeners.OnMapStart>(OnMapStart);
        RemoveCommand("css_plugintoggle", OnToggleCommand);
    }

    private void OnMapStart(string mapName)
    {
        ArmApply(mapName);
    }

    private void ArmApply(string? mapName)
    {
        if (!string.IsNullOrEmpty(mapName))
            _pendingMap = mapName;

        if (_worldApplyArmed)
            return;

        _worldApplyArmed = true;
        RegisterListener<Listeners.OnServerPreWorldUpdate>(OnWorldApply);
        Logger.LogInformation(
            "Waiting for OnServerPreWorldUpdate; pending='{Pending}' server='{Map}'",
            _pendingMap ?? "",
            Server.MapName ?? "");
    }

    private void OnWorldApply(bool simulating)
    {
        var map = !string.IsNullOrEmpty(_pendingMap) ? _pendingMap : (Server.MapName ?? "");
        if (string.IsNullOrEmpty(map))
            return;

        _pendingMap = null;
        DisarmWorldApply();
        Logger.LogInformation("OnServerPreWorldUpdate simulating={Sim} map='{Map}'", simulating, map);
        ApplyForMap(map);
    }

    private void DisarmWorldApply()
    {
        if (!_worldApplyArmed)
            return;
        _worldApplyArmed = false;
        RemoveListener<Listeners.OnServerPreWorldUpdate>(OnWorldApply);
    }

    private void ApplyForMap(string mapName)
    {
        _lastMap = mapName ?? "";
        DropStaleHeld(_blacklist, _blacklistHeld);
        DropStaleHeld(_whitelist, _whitelistHeld);
        _blacklistMatch = IsMatch(_lastMap, _blacklist.MapTokens);
        _whitelistMatch = IsMatch(_lastMap, _whitelist.MapTokens);

        if (_settings.UsesBlacklist)
        {
            if (_blacklistMatch)
            {
                Logger.LogInformation("Map '{Map}' matched blacklist; unloading configured plugins.", _lastMap);
                UnloadList(_blacklist, _blacklistHeld);
            }
            else
            {
                Logger.LogInformation("Map '{Map}' did not match blacklist; restoring held plugins.", _lastMap);
                if (_settings.ReloadOnLeave)
                    ReloadHeld(_blacklist, _blacklistHeld);
            }
        }
        else
        {
            ReloadHeld(_blacklist, _blacklistHeld);
        }

        if (_settings.UsesWhitelist)
        {
            if (_whitelistMatch)
            {
                Logger.LogInformation("Map '{Map}' matched whitelist; restoring held plugins.", _lastMap);
                ReloadHeld(_whitelist, _whitelistHeld);
            }
            else
            {
                Logger.LogInformation("Map '{Map}' did not match whitelist; unloading configured plugins.", _lastMap);
                UnloadList(_whitelist, _whitelistHeld);
            }
        }
        else
        {
            ReloadHeld(_whitelist, _whitelistHeld);
        }
    }

    private static bool IsMatch(string mapName, List<string> tokens)
    {
        if (string.IsNullOrEmpty(mapName) || tokens.Count == 0)
            return false;

        foreach (var token in tokens)
        {
            if (mapName.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void UnloadList(PluginToggleList list, HashSet<string> held)
    {
        foreach (var target in list.Plugins)
        {
            if (IsForbidden(target) || IsConflict(target))
            {
                Logger.LogWarning(
                    "Skipping plugin '{Name}' (unloadAs={UnloadAs}, forbidden={Forbidden}, conflict={Conflict}).",
                    target.Name,
                    target.UnloadName,
                    IsForbidden(target),
                    IsConflict(target));
                continue;
            }

            if (!held.Add(target.Name))
                continue;

            Logger.LogInformation("css_plugins unload {UnloadAs} (load={Name})", target.UnloadName, target.Name);
            Server.ExecuteCommand($"css_plugins unload \"{Escape(target.UnloadName)}\"");
        }
    }

    private void DropStaleHeld(PluginToggleList list, HashSet<string> held)
    {
        if (held.Count == 0)
            return;

        var names = new HashSet<string>(list.Plugins.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var name in held.Where(n => !names.Contains(n)).ToList())
        {
            Logger.LogInformation("css_plugins load {Name} (removed from list)", name);
            Server.ExecuteCommand($"css_plugins load \"{Escape(name)}\"");
            held.Remove(name);
        }
    }

    private void ReloadHeld(PluginToggleList list, HashSet<string> held)
    {
        if (held.Count == 0)
            return;

        foreach (var target in list.Plugins)
        {
            if (!held.Contains(target.Name))
                continue;
            if (IsForbidden(target) || IsConflict(target))
                continue;

            Logger.LogInformation("css_plugins load {Name}", target.Name);
            Server.ExecuteCommand($"css_plugins load \"{Escape(target.Name)}\"");
        }

        held.Clear();
    }

    private bool IsForbidden(PluginToggleTarget target)
    {
        if (string.Equals(target.Name, ModuleName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(target.UnloadName, ModuleName, StringComparison.OrdinalIgnoreCase))
            return true;
        return NeverTouch.Contains(target.Name) || NeverTouch.Contains(target.UnloadName);
    }

    private bool IsConflict(PluginToggleTarget target) =>
        _conflicts.Contains(target.Name) || _conflicts.Contains(target.UnloadName);

    private void RebuildConflicts()
    {
        _conflicts.Clear();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void AddKeys(PluginToggleList list, string side)
        {
            foreach (var target in list.Plugins)
            {
                Remember(target.Name, side);
                if (!string.Equals(target.UnloadName, target.Name, StringComparison.OrdinalIgnoreCase))
                    Remember(target.UnloadName, side);
            }
        }

        void Remember(string key, string side)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;
            if (!seen.TryGetValue(key, out var existing))
            {
                seen[key] = side;
                return;
            }

            if (string.Equals(existing, side, StringComparison.OrdinalIgnoreCase))
                return;

            _conflicts.Add(key);
            Logger.LogWarning("Plugin '{Key}' is in both blacklist and whitelist; skipping it.", key);
        }

        AddKeys(_blacklist, "blacklist");
        AddKeys(_whitelist, "whitelist");
    }

    private static string Escape(string name) => name.Replace("\"", "");

    private bool TryLoadFiles(out string error)
    {
        error = "";
        try
        {
            Directory.CreateDirectory(ConfigDir);
            MigrateLegacyIfNeeded();
            EnsureDefaultFiles();

            _settings = ReadJson<PluginToggleSettings>(Path.Combine(ConfigDir, ConfigFileName))
                ?? PluginToggleSettings.Default();
            _blacklist = ReadJson<PluginToggleList>(Path.Combine(ConfigDir, BlacklistFileName))
                ?? PluginToggleList.DefaultBlacklist();
            _whitelist = ReadJson<PluginToggleList>(Path.Combine(ConfigDir, WhitelistFileName))
                ?? PluginToggleList.DefaultWhitelist();

            _settings.Normalize();
            _blacklist.Normalize();
            _whitelist.Normalize();
            RebuildConflicts();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _settings = PluginToggleSettings.Default();
            _settings.Normalize();
            _blacklist = new PluginToggleList();
            _whitelist = new PluginToggleList();
            _conflicts.Clear();
            return false;
        }
    }

    private void MigrateLegacyIfNeeded()
    {
        var configPath = Path.Combine(ConfigDir, ConfigFileName);
        if (File.Exists(configPath))
            return;

        var legacyPath = Path.Combine(ConfigDir, LegacyFileName);
        if (!File.Exists(legacyPath))
            return;

        var legacy = ReadJson<LegacyPluginToggleFile>(legacyPath);
        var settings = PluginToggleSettings.Default();
        if (legacy != null)
        {
            settings.ReloadOnLeave = legacy.ReloadOnLeave;
            settings.ApplyDelaySeconds = legacy.ApplyDelaySeconds;
        }

        var blacklist = PluginToggleList.DefaultBlacklist();
        if (legacy != null)
        {
            var tokens = PluginToggleList.NormalizeTokens(legacy.MapTokens);
            var plugins = PluginToggleList.NormalizePlugins(legacy.Plugins);
            if (tokens.Count > 0)
                blacklist.MapTokens = tokens;
            if (plugins.Count > 0)
                blacklist.Plugins = plugins;
        }

        WriteJson(configPath, settings);
        WriteJson(Path.Combine(ConfigDir, BlacklistFileName), blacklist);
        if (!File.Exists(Path.Combine(ConfigDir, WhitelistFileName)))
            WriteJson(Path.Combine(ConfigDir, WhitelistFileName), PluginToggleList.DefaultWhitelist());

        var bak = legacyPath + ".bak";
        if (File.Exists(bak))
            File.Delete(bak);
        File.Move(legacyPath, bak);
        Logger.LogInformation("Migrated {Legacy} to config/blacklist/whitelist; backup {Bak}", legacyPath, bak);
    }

    private void EnsureDefaultFiles()
    {
        var configPath = Path.Combine(ConfigDir, ConfigFileName);
        var blacklistPath = Path.Combine(ConfigDir, BlacklistFileName);
        var whitelistPath = Path.Combine(ConfigDir, WhitelistFileName);

        if (!File.Exists(configPath))
            WriteJson(configPath, PluginToggleSettings.Default());
        if (!File.Exists(blacklistPath))
            WriteJson(blacklistPath, PluginToggleList.DefaultBlacklist());
        if (!File.Exists(whitelistPath))
            WriteJson(whitelistPath, PluginToggleList.DefaultWhitelist());
    }

    private static T? ReadJson<T>(string path) where T : class
    {
        if (!File.Exists(path))
            return null;
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return JsonSerializer.Deserialize<T>(text, JsonOpts);
    }

    private static void WriteJson<T>(string path, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOpts);
        File.WriteAllText(path, json + Environment.NewLine);
    }

    private void OnToggleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null && player.IsValid && !AdminManager.PlayerHasPermissions(player, "@css/generic"))
        {
            command.ReplyToCommand("[PluginToggle] no permission");
            return;
        }

        var sub = command.ArgCount > 1 ? command.ArgByIndex(1) : "";
        if (sub.Equals("reload", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryLoadFiles(out var error))
            {
                command.ReplyToCommand($"[PluginToggle] reload failed: {error}");
                return;
            }

            var map = Server.MapName ?? _lastMap;
            Logger.LogInformation("css_plugintoggle reload; applying map='{Map}' mode={Mode}", map, _settings.Mode);
            if (!string.IsNullOrEmpty(map))
                ApplyForMap(map);
            ReplyStatus(command);
            return;
        }

        ReplyStatus(command);
    }

    private void ReplyStatus(CommandInfo command)
    {
        command.ReplyToCommand(
            $"[PluginToggle] mode={_settings.Mode} map='{_lastMap}'");
        command.ReplyToCommand(
            $"[PluginToggle] blacklist match={_blacklistMatch} held=[{string.Join(",", _blacklistHeld)}] tokens=[{string.Join(",", _blacklist.MapTokens)}]");
        command.ReplyToCommand(
            $"[PluginToggle] whitelist match={_whitelistMatch} held=[{string.Join(",", _whitelistHeld)}] tokens=[{string.Join(",", _whitelist.MapTokens)}]");
    }
}
