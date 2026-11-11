using System.Text.Json.Serialization;

namespace PluginToggle;

public enum PluginToggleMode
{
    Blacklist,
    Whitelist,
    Both,
}

public sealed class PluginToggleTarget
{
    /// <summary>Directory name under plugins/ used by css_plugins load.</summary>
    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    /// <summary>Argument for css_plugins unload. Empty means use Name.</summary>
    [JsonPropertyName("UnloadAs")]
    public string UnloadAs { get; set; } = "";

    [JsonIgnore]
    public string UnloadName => string.IsNullOrWhiteSpace(UnloadAs) ? Name : UnloadAs;
}

public sealed class PluginToggleList
{
    [JsonPropertyName("MapTokens")]
    public List<string> MapTokens { get; set; } = [];

    [JsonPropertyName("Plugins")]
    public List<PluginToggleTarget> Plugins { get; set; } = [];

    public void Normalize()
    {
        MapTokens = NormalizeTokens(MapTokens);
        Plugins = NormalizePlugins(Plugins);
    }

    public static List<string> NormalizeTokens(IEnumerable<string>? tokens) =>
        (tokens ?? [])
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static List<PluginToggleTarget> NormalizePlugins(IEnumerable<PluginToggleTarget>? plugins) =>
        (plugins ?? [])
            .Select(p => new PluginToggleTarget
            {
                Name = (p.Name ?? "").Trim(),
                UnloadAs = (p.UnloadAs ?? "").Trim(),
            })
            .Where(p => p.Name.Length > 0)
            .ToList();

    public static PluginToggleList DefaultBlacklist() => new()
    {
        MapTokens = ["3171695956", "cabin"],
        Plugins =
        [
            new() { Name = "BotBuy", UnloadAs = "BotBuyPatch" },
            new() { Name = "BotOrders" },
            new() { Name = "BotRandomizer" },
            new() { Name = "BotChat" },
        ],
    };

    public static PluginToggleList DefaultWhitelist() => new()
    {
        MapTokens = ["3171695956", "cabin"],
        Plugins =
        [
            new() { Name = "ZombieCabinDefence", UnloadAs = "Zombie Cabin Defense" },
        ],
    };
}

public sealed class PluginToggleSettings
{
    [JsonPropertyName("Mode")]
    public string Mode { get; set; } = "both";

    [JsonPropertyName("ReloadOnLeave")]
    public bool ReloadOnLeave { get; set; } = true;

    [JsonPropertyName("ApplyDelaySeconds")]
    public float ApplyDelaySeconds { get; set; } = 0.35f;

    [JsonIgnore]
    public PluginToggleMode ParsedMode { get; private set; } = PluginToggleMode.Both;

    [JsonIgnore]
    public bool UsesBlacklist =>
        ParsedMode is PluginToggleMode.Blacklist or PluginToggleMode.Both;

    [JsonIgnore]
    public bool UsesWhitelist =>
        ParsedMode is PluginToggleMode.Whitelist or PluginToggleMode.Both;

    public void Normalize()
    {
        ApplyDelaySeconds = Math.Clamp(ApplyDelaySeconds, 0f, 5f);
        ParsedMode = ParseMode(Mode);
        Mode = ParsedMode switch
        {
            PluginToggleMode.Blacklist => "blacklist",
            PluginToggleMode.Whitelist => "whitelist",
            _ => "both",
        };
    }

    public static PluginToggleMode ParseMode(string? raw)
    {
        if (string.Equals(raw, "blacklist", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "black", StringComparison.OrdinalIgnoreCase))
            return PluginToggleMode.Blacklist;

        if (string.Equals(raw, "whitelist", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "white", StringComparison.OrdinalIgnoreCase))
            return PluginToggleMode.Whitelist;

        return PluginToggleMode.Both;
    }

    public static PluginToggleSettings Default() => new()
    {
        Mode = "both",
        ReloadOnLeave = true,
        ApplyDelaySeconds = 0.35f,
    };
}

public sealed class PluginToggleProfile
{
    [JsonPropertyName("Blacklist")]
    public List<PluginToggleTarget> Blacklist { get; set; } = [];

    [JsonPropertyName("Whitelist")]
    public List<PluginToggleTarget> Whitelist { get; set; } = [];

    public void Normalize()
    {
        Blacklist = PluginToggleList.NormalizePlugins(Blacklist);
        Whitelist = PluginToggleList.NormalizePlugins(Whitelist);
    }
}

public sealed class PluginToggleProfilesFile
{
    [JsonPropertyName("Profiles")]
    public Dictionary<string, PluginToggleProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        var next = new Dictionary<string, PluginToggleProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Profiles ?? new Dictionary<string, PluginToggleProfile>())
        {
            var key = (pair.Key ?? "").Trim();
            if (key.Length == 0 || pair.Value == null)
                continue;
            pair.Value.Normalize();
            next[key] = pair.Value;
        }

        Profiles = next;
    }

    public static PluginToggleProfilesFile Default() => new()
    {
        Profiles = new Dictionary<string, PluginToggleProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["funmatch"] = new PluginToggleProfile
            {
                Blacklist =
                [
                    new() { Name = "AdminPlus" },
                    new() { Name = "BotBuy", UnloadAs = "BotBuyPatch" },
                    new() { Name = "BotOrders" },
                    new() { Name = "BotChat" },
                    new() { Name = "NadeSystem" },
                    new() { Name = "Banter" },
                    new() { Name = "Smarter-Bot" },
                ],
                Whitelist =
                [
                    new() { Name = "FunMatchPlugin", UnloadAs = "Fun Match Plugin" },
                ],
            },
        },
    };

    public static PluginToggleProfilesFile Empty() => new()
    {
        Profiles = new Dictionary<string, PluginToggleProfile>(StringComparer.OrdinalIgnoreCase),
    };
}

internal sealed class LegacyPluginToggleFile
{
    [JsonPropertyName("MapTokens")]
    public List<string>? MapTokens { get; set; }

    [JsonPropertyName("Plugins")]
    public List<PluginToggleTarget>? Plugins { get; set; }

    [JsonPropertyName("ReloadOnLeave")]
    public bool ReloadOnLeave { get; set; } = true;

    [JsonPropertyName("ApplyDelaySeconds")]
    public float ApplyDelaySeconds { get; set; } = 0.35f;
}
