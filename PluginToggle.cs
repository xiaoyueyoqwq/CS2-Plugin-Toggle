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
    public override string ModuleDescription => "Unload or load CSS plugins by map and optional mode profile";
    public override string ModuleAuthor => "xiaoyueyoqwq";
    public override string ModuleVersion => "1.2.0";

    private const string ConfigFileName = "config.json";
    private const string BlacklistFileName = "blacklist.json";
    private const string WhitelistFileName = "whitelist.json";
    private const string ProfilesFileName = "profiles.json";
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
    private Dictionary<string, PluginToggleProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _conflicts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blacklistHeld = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _whitelistHeld = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _profileBlacklistHeld = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _profileWhitelistHeld = new(StringComparer.OrdinalIgnoreCase);
    private string _lastMap = "";
    private string _activeProfile = "";
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
            "PluginToggle {Version} loaded. mode={Mode} profile='{Profile}' blacklist=[{BlTokens}/{BlPlugins}] whitelist=[{WlTokens}/{WlPlugins}] profiles=[{Profiles}] reloadOnLeave={Reload}",
            ModuleVersion,
            _settings.Mode,
            _activeProfile,
            string.Join(",", _blacklist.MapTokens),
            string.Join(",", _blacklist.Plugins.Select(p => p.Name)),
            string.Join(",", _whitelist.MapTokens),
            string.Join(",", _whitelist.Plugins.Select(p => p.Name)),
            string.Join(",", _profiles.Keys),
            _settings.ReloadOnLeave);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        Logger.LogInformation(
            "OnAllPluginsLoaded hotReload={Hot} map='{Map}' profile='{Profile}'",
            hotReload,
            Server.MapName ?? "",
            _activeProfile);
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
            "Waiting for OnServerPreWorldUpdate; pending='{Pending}' server='{Map}' profile='{Profile}'",
            _pendingMap ?? "",
            Server.MapName ?? "",
            _activeProfile);
    }

    private void OnWorldApply(bool simulating)
    {
        var map = !string.IsNullOrEmpty(_pendingMap) ? _pendingMap : (Server.MapName ?? "");
        if (string.IsNullOrEmpty(map))
            return;

        _pendingMap = null;
        DisarmWorldApply();
        Logger.LogInformation(
            "OnServerPreWorldUpdate simulating={Sim} map='{Map}' profile='{Profile}'",
            simulating,
            map,
            _activeProfile);
        Apply(map);
    }

    private void DisarmWorldApply()
    {
        if (!_worldApplyArmed)
            return;
        _worldApplyArmed = false;
        RemoveListener<Listeners.OnServerPreWorldUpdate>(OnWorldApply);
    }

    private void Apply(string mapName)
    {
        _lastMap = mapName ?? "";
        DropStaleHeld(_blacklist, _blacklistHeld);
        DropStaleHeld(_whitelist, _whitelistHeld);
        DropStaleProfileHeld();

        _blacklistMatch = IsMatch(_lastMap, _blacklist.MapTokens);
        _whitelistMatch = IsMatch(_lastMap, _whitelist.MapTokens);

        var unload = new Dictionary<string, PluginToggleTarget>(StringComparer.OrdinalIgnoreCase);
        var load = new Dictionary<string, PluginToggleTarget>(StringComparer.OrdinalIgnoreCase);

        void WantUnload(PluginToggleTarget target, bool profileLayer)
        {
            if (IsForbidden(target, profileLayer) || IsConflict(target))
            {
                Logger.LogWarning(
                    "Skipping plugin '{Name}' (unloadAs={UnloadAs}, forbidden={Forbidden}, conflict={Conflict}, profileLayer={ProfileLayer}).",
                    target.Name,
                    target.UnloadName,
                    IsForbidden(target, profileLayer),
                    IsConflict(target),
                    profileLayer);
                return;
            }

            unload[target.Name] = target;
            load.Remove(target.Name);
        }

        void WantLoad(PluginToggleTarget target, bool profileLayer)
        {
            if (IsForbidden(target, profileLayer) || IsConflict(target))
                return;
            if (unload.ContainsKey(target.Name))
                return;
            load[target.Name] = target;
        }

        if (_settings.UsesBlacklist)
        {
            if (_blacklistMatch)
            {
                Logger.LogInformation("Map '{Map}' matched blacklist; unloading configured plugins.", _lastMap);
                foreach (var target in _blacklist.Plugins)
                {
                    WantUnload(target, profileLayer: false);
                    if (!IsForbidden(target, false) && !IsConflict(target))
                        _blacklistHeld.Add(target.Name);
                }
            }
            else if (_settings.ReloadOnLeave)
            {
                Logger.LogInformation("Map '{Map}' did not match blacklist; restoring held plugins.", _lastMap);
                foreach (var target in _blacklist.Plugins)
                {
                    if (_blacklistHeld.Contains(target.Name))
                        WantLoad(target, profileLayer: false);
                }
            }
        }
        else
        {
            foreach (var target in _blacklist.Plugins)
            {
                if (_blacklistHeld.Contains(target.Name))
                    WantLoad(target, profileLayer: false);
            }
        }

        if (_settings.UsesWhitelist)
        {
            if (_whitelistMatch)
            {
                Logger.LogInformation("Map '{Map}' matched whitelist; restoring held plugins.", _lastMap);
                foreach (var target in _whitelist.Plugins)
                {
                    if (_whitelistHeld.Contains(target.Name))
                        WantLoad(target, profileLayer: false);
                }
            }
            else
            {
                Logger.LogInformation("Map '{Map}' did not match whitelist; unloading configured plugins.", _lastMap);
                foreach (var target in _whitelist.Plugins)
                {
                    WantUnload(target, profileLayer: false);
                    if (!IsForbidden(target, false) && !IsConflict(target))
                        _whitelistHeld.Add(target.Name);
                }
            }
        }
        else
        {
            foreach (var target in _whitelist.Plugins)
            {
                if (_whitelistHeld.Contains(target.Name))
                    WantLoad(target, profileLayer: false);
            }
        }

        foreach (var pair in _profiles)
        {
            var active = string.Equals(pair.Key, _activeProfile, StringComparison.OrdinalIgnoreCase);
            var profileHeldBl = HeldFor(_profileBlacklistHeld, pair.Key);
            var profileHeldWl = HeldFor(_profileWhitelistHeld, pair.Key);

            if (active)
            {
                Logger.LogInformation("Profile '{Profile}' active; applying profile lists.", pair.Key);
                foreach (var target in pair.Value.Blacklist)
                {
                    WantUnload(target, profileLayer: true);
                    if (!IsForbidden(target, true) && !IsConflict(target))
                        profileHeldBl.Add(target.Name);
                }

                foreach (var target in pair.Value.Whitelist)
                    WantLoad(target, profileLayer: true);
            }
            else
            {
                foreach (var target in pair.Value.Blacklist)
                {
                    if (profileHeldBl.Contains(target.Name))
                        WantLoad(target, profileLayer: true);
                }

                foreach (var target in pair.Value.Whitelist)
                {
                    WantUnload(target, profileLayer: true);
                    if (!IsForbidden(target, true) && !IsConflict(target))
                        profileHeldWl.Add(target.Name);
                }
            }
        }

        foreach (var target in unload.Values)
        {
            Logger.LogInformation("css_plugins unload {UnloadAs} (load={Name})", target.UnloadName, target.Name);
            Server.ExecuteCommand($"css_plugins unload \"{Escape(target.UnloadName)}\"");
        }

        foreach (var target in load.Values)
        {
            Logger.LogInformation("css_plugins load {Name}", target.Name);
            Server.ExecuteCommand($"css_plugins load \"{Escape(target.Name)}\"");
            _blacklistHeld.Remove(target.Name);
            _whitelistHeld.Remove(target.Name);
            foreach (var held in _profileBlacklistHeld.Values)
                held.Remove(target.Name);
            foreach (var held in _profileWhitelistHeld.Values)
                held.Remove(target.Name);
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

    private static HashSet<string> HeldFor(Dictionary<string, HashSet<string>> store, string profile)
    {
        if (!store.TryGetValue(profile, out var held))
        {
            held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            store[profile] = held;
        }

        return held;
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

    private void DropStaleProfileHeld()
    {
        foreach (var profile in _profileBlacklistHeld.Keys.ToList())
        {
            if (!_profiles.TryGetValue(profile, out var def))
            {
                foreach (var name in _profileBlacklistHeld[profile])
                {
                    Logger.LogInformation("css_plugins load {Name} (removed profile {Profile})", name, profile);
                    Server.ExecuteCommand($"css_plugins load \"{Escape(name)}\"");
                }

                _profileBlacklistHeld.Remove(profile);
                continue;
            }

            DropStaleNames(def.Blacklist, _profileBlacklistHeld[profile]);
        }

        foreach (var profile in _profileWhitelistHeld.Keys.ToList())
        {
            if (!_profiles.TryGetValue(profile, out var def))
            {
                _profileWhitelistHeld.Remove(profile);
                continue;
            }

            DropStaleNames(def.Whitelist, _profileWhitelistHeld[profile], loadRemoved: false);
        }
    }

    private void DropStaleNames(List<PluginToggleTarget> plugins, HashSet<string> held, bool loadRemoved = true)
    {
        var names = new HashSet<string>(plugins.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var name in held.Where(n => !names.Contains(n)).ToList())
        {
            if (loadRemoved)
            {
                Logger.LogInformation("css_plugins load {Name} (removed from profile list)", name);
                Server.ExecuteCommand($"css_plugins load \"{Escape(name)}\"");
            }

            held.Remove(name);
        }
    }

    private bool IsForbidden(PluginToggleTarget target, bool profileLayer)
    {
        if (string.Equals(target.Name, ModuleName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(target.UnloadName, ModuleName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (profileLayer && IsAdminPlus(target))
            return false;
        return NeverTouch.Contains(target.Name) || NeverTouch.Contains(target.UnloadName);
    }

    private static bool IsAdminPlus(PluginToggleTarget target) =>
        string.Equals(target.Name, "AdminPlus", StringComparison.OrdinalIgnoreCase)
        || string.Equals(target.UnloadName, "AdminPlus", StringComparison.OrdinalIgnoreCase);

    private bool IsConflict(PluginToggleTarget target) =>
        _conflicts.Contains(target.Name) || _conflicts.Contains(target.UnloadName);

    private void RebuildConflicts()
    {
        _conflicts.Clear();

        void FlagPair(IEnumerable<PluginToggleTarget> blacklist, IEnumerable<PluginToggleTarget> whitelist, string label)
        {
            var black = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in blacklist)
            {
                if (target.Name.Length > 0)
                    black.Add(target.Name);
                if (target.UnloadName.Length > 0)
                    black.Add(target.UnloadName);
            }

            foreach (var target in whitelist)
            {
                if (black.Contains(target.Name) || black.Contains(target.UnloadName))
                {
                    if (target.Name.Length > 0)
                        _conflicts.Add(target.Name);
                    if (target.UnloadName.Length > 0)
                        _conflicts.Add(target.UnloadName);
                    Logger.LogWarning("Plugin '{Key}' is in both blacklist and whitelist ({Label}); skipping it.", target.Name, label);
                }
            }
        }

        FlagPair(_blacklist.Plugins, _whitelist.Plugins, "map");
        foreach (var pair in _profiles)
            FlagPair(pair.Value.Blacklist, pair.Value.Whitelist, $"profile {pair.Key}");
    }

    private static string Escape(string name) => name.Replace("\"", "");

    private static string NormalizeProfileName(string? raw)
    {
        var name = (raw ?? "").Trim();
        if (name.Length == 0 || name == "-")
            return "";
        return name;
    }

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
            var profilesFile = ReadJson<PluginToggleProfilesFile>(Path.Combine(ConfigDir, ProfilesFileName))
                ?? PluginToggleProfilesFile.Empty();

            _settings.Normalize();
            _blacklist.Normalize();
            _whitelist.Normalize();
            profilesFile.Normalize();
            _profiles = profilesFile.Profiles;
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
            _profiles = new Dictionary<string, PluginToggleProfile>(StringComparer.OrdinalIgnoreCase);
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
        if (!File.Exists(Path.Combine(ConfigDir, ProfilesFileName)))
            WriteJson(Path.Combine(ConfigDir, ProfilesFileName), PluginToggleProfilesFile.Empty());

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
        var profilesPath = Path.Combine(ConfigDir, ProfilesFileName);

        if (!File.Exists(configPath))
            WriteJson(configPath, PluginToggleSettings.Default());
        if (!File.Exists(blacklistPath))
            WriteJson(blacklistPath, PluginToggleList.DefaultBlacklist());
        if (!File.Exists(whitelistPath))
            WriteJson(whitelistPath, PluginToggleList.DefaultWhitelist());
        if (!File.Exists(profilesPath))
            WriteJson(profilesPath, PluginToggleProfilesFile.Empty());
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
            Logger.LogInformation(
                "css_plugintoggle reload; applying map='{Map}' mode={Mode} profile='{Profile}'",
                map,
                _settings.Mode,
                _activeProfile);
            if (!string.IsNullOrEmpty(map))
                Apply(map);
            ReplyStatus(command);
            return;
        }

        if (sub.Equals("profile", StringComparison.OrdinalIgnoreCase))
        {
            var raw = command.ArgCount > 2 ? command.ArgByIndex(2) : "";
            if (raw.Length == 0)
            {
                ReplyStatus(command);
                return;
            }

            var next = NormalizeProfileName(raw);
            if (next.Length > 0 && !_profiles.ContainsKey(next))
                Logger.LogWarning("css_plugintoggle profile '{Profile}' is not in profiles.json; stored until reload", next);

            _activeProfile = next;
            Logger.LogInformation("css_plugintoggle profile='{Profile}'", _activeProfile);
            // Do not apply here: Switch sets the name then changelevel in the same
            // tick. Apply on OnMapStart / reload so the old round is not touched.
            ReplyStatus(command);
            return;
        }

        ReplyStatus(command);
    }

    private void ReplyStatus(CommandInfo command)
    {
        command.ReplyToCommand(
            $"[PluginToggle] mode={_settings.Mode} map='{_lastMap}' profile='{_activeProfile}'");
        command.ReplyToCommand(
            $"[PluginToggle] blacklist match={_blacklistMatch} held=[{string.Join(",", _blacklistHeld)}] tokens=[{string.Join(",", _blacklist.MapTokens)}]");
        command.ReplyToCommand(
            $"[PluginToggle] whitelist match={_whitelistMatch} held=[{string.Join(",", _whitelistHeld)}] tokens=[{string.Join(",", _whitelist.MapTokens)}]");
        command.ReplyToCommand(
            $"[PluginToggle] profiles=[{string.Join(",", _profiles.Keys)}]");
    }
}
