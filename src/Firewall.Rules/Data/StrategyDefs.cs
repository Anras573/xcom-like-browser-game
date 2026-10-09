using System.Text.Json.Serialization;

namespace Firewall.Rules.Data;

public sealed record FacilityUpgradeDef : IDef
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public string Name { get; init; } = "";

    [JsonRequired]
    public int Cost { get; init; }
    public string? Requires { get; init; }
    private readonly IReadOnlyDictionary<string, double>? _effects;
    public IReadOnlyDictionary<string, double> Effects
    {
        get => _effects ?? Maps.Empty;
        init => _effects = value;
    }
}

public sealed record FacilityDef : IDef
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public string Name { get; init; } = "";

    [JsonRequired]
    public int Cost { get; init; }

    [JsonRequired]
    public int BuildDays { get; init; }

    [JsonRequired]
    public string Description { get; init; } = "";

    /// <summary>Pre-built facilities exist from the start and use no build slot.</summary>
    public bool PreBuilt { get; init; }

    /// <summary>How many can be built (stacking facilities allow more than 1).</summary>
    [JsonRequired]
    public int MaxCount { get; init; }

    private readonly IReadOnlyDictionary<string, double>? _effects;
    public IReadOnlyDictionary<string, double> Effects
    {
        get => _effects ?? Maps.Empty;
        init => _effects = value;
    }
    private readonly IReadOnlyList<FacilityUpgradeDef>? _upgrades;
    public IReadOnlyList<FacilityUpgradeDef> Upgrades
    {
        get => _upgrades ?? [];
        init => _upgrades = value;
    }
}

public sealed record ResearchRequirement
{
    [JsonRequired]
    public RequirementKind Kind { get; init; }

    /// <summary>Research, enemy or mission id, or key item name; unused for resources.</summary>
    public string? Id { get; init; }
    public int Amount { get; init; }
}

public sealed record ResearchDef : IDef
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public string Name { get; init; } = "";

    [JsonRequired]
    public string Description { get; init; } = "";

    [JsonRequired]
    public int BaseDays { get; init; }
    public bool Story { get; init; }
    private readonly IReadOnlyList<ResearchRequirement>? _requires;
    public IReadOnlyList<ResearchRequirement> Requires
    {
        get => _requires ?? [];
        init => _requires = value;
    }

    /// <summary>Ids of items, armor, workshop entries or missions this research unlocks.</summary>
    private readonly IReadOnlyList<string>? _unlocks;
    public IReadOnlyList<string> Unlocks
    {
        get => _unlocks ?? [];
        init => _unlocks = value;
    }

    private readonly IReadOnlyDictionary<string, double>? _effects;
    public IReadOnlyDictionary<string, double> Effects
    {
        get => _effects ?? Maps.Empty;
        init => _effects = value;
    }
}

public sealed record WorkshopItemDef : IDef
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public string Name { get; init; } = "";

    [JsonRequired]
    public WorkshopKind Kind { get; init; }

    /// <summary>Item or armor id built; for tier upgrades the weapon tier it grants.</summary>
    public string? TargetId { get; init; }
    public int Tier { get; init; }
    public int Credits { get; init; }
    public int Salvage { get; init; }
    public int Cores { get; init; }
    public int MinEngineers { get; init; }

    /// <summary>0 = built instantly.</summary>
    public int BuildDays { get; init; }
}

public sealed record MissionTypeDef : IDef
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public string Name { get; init; } = "";

    [JsonRequired]
    public string Goal { get; init; } = "";

    [JsonRequired]
    public string Fail { get; init; } = "";
    public bool Story { get; init; }

    /// <summary>Item id that must exist before the mission is offered.</summary>
    public string? RequiresItem { get; init; }

    private readonly IReadOnlyDictionary<string, double>? _parameters;
    public IReadOnlyDictionary<string, double> Parameters
    {
        get => _parameters ?? Maps.Empty;
        init => _parameters = value;
    }
}

public sealed record DistrictDef : IDef
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public string Name { get; init; } = "";

    [JsonRequired]
    public Biome Biome { get; init; }

    /// <summary>Locked districts only host a story mission.</summary>
    public bool Locked { get; init; }
}

public sealed record NameLists
{
    private readonly IReadOnlyList<string>? _firstNames;

    [JsonRequired]
    public IReadOnlyList<string> FirstNames
    {
        get => _firstNames ?? [];
        init => _firstNames = value;
    }

    private readonly IReadOnlyList<string>? _lastNames;

    [JsonRequired]
    public IReadOnlyList<string> LastNames
    {
        get => _lastNames ?? [];
        init => _lastNames = value;
    }

    private readonly IReadOnlyList<string>? _nicknames;

    [JsonRequired]
    public IReadOnlyList<string> Nicknames
    {
        get => _nicknames ?? [];
        init => _nicknames = value;
    }
}
