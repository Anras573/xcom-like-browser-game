using System.Text.Json.Serialization;

namespace Firewall.Rules.Data;

public sealed record SpriteDef
{
    /// <summary>Character sprite set, such as <c>zombie1</c> or <c>robot1</c>.</summary>
    [JsonRequired]
    public string Set { get; init; } = "";

    [JsonRequired]
    public string Pose { get; init; } = "";

    /// <summary>Tint as <c>#rrggbb</c>; null for none.</summary>
    public string? Tint { get; init; }

    [JsonRequired]
    public double Scale { get; init; }
}

public sealed record BehaviorDef
{
    [JsonRequired]
    public AiKind Kind { get; init; }
    public bool UsesCover { get; init; }
    public bool Melee { get; init; }
    public bool PrefersCivilians { get; init; }

    /// <summary>Overwatches when it has no shot.</summary>
    public bool Overwatches { get; init; }

    /// <summary>Multipliers for the utility scoring (§4.9); 1 is neutral.</summary>
    [JsonRequired]
    public double FlankWeight { get; init; }

    [JsonRequired]
    public double CoverWeight { get; init; }
    public int PreferredRangeMin { get; init; }
    public int PreferredRangeMax { get; init; }
}

public sealed record LootEntry
{
    [JsonRequired]
    public Resource Resource { get; init; }

    [JsonRequired]
    public int Amount { get; init; }

    [JsonRequired]
    public int ChancePercent { get; init; }
}

public sealed record SummonDef
{
    [JsonRequired]
    public string EnemyId { get; init; } = "";

    [JsonRequired]
    public int Count { get; init; }

    /// <summary>Summons every N turns (0 = not periodic).</summary>
    public int EveryTurns { get; init; }

    /// <summary>Summons once when HP drops to this percent or below (0 = not HP-triggered).</summary>
    public int BelowHpPercent { get; init; }
}

public sealed record EnemyDef : IDef
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public string Name { get; init; } = "";
    public EnemyKind Kind { get; init; }
    public bool Robot { get; init; }

    /// <summary>Robot tier used by Hack (0 for non-robots).</summary>
    public int Tier { get; init; }

    [JsonRequired]
    public int Hp { get; init; }
    public int Aim { get; init; }
    public int DmgMin { get; init; }
    public int DmgMax { get; init; }
    public int Mobility { get; init; }
    public int Defense { get; init; }
    public int Armor { get; init; }
    public int Sight { get; init; }

    [JsonRequired]
    public int PodMin { get; init; }

    [JsonRequired]
    public int PodMax { get; init; }
    public bool Capturable { get; init; }

    [JsonRequired]
    public BehaviorDef Behavior { get; init; } = null!;
    private readonly IReadOnlyList<string>? _abilities;
    public IReadOnlyList<string> Abilities
    {
        get => _abilities ?? [];
        init => _abilities = value;
    }
    public SummonDef? Summon { get; init; }

    [JsonRequired]
    public SpriteDef Sprite { get; init; } = null!;
    private readonly IReadOnlyList<LootEntry>? _loot;
    public IReadOnlyList<LootEntry> Loot
    {
        get => _loot ?? [];
        init => _loot = value;
    }
}

/// <summary>A tinted stronger variant rolled as Convergence rises (§5.6).</summary>
public sealed record EliteDef : IDef
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public string Name { get; init; } = "";

    [JsonRequired]
    public int MinConvergence { get; init; }

    [JsonRequired]
    public int Hp { get; init; }

    [JsonRequired]
    public int Aim { get; init; }
    public int Armor { get; init; }

    [JsonRequired]
    public string Tint { get; init; } = "";

    [JsonRequired]
    public double LootMultiplier { get; init; }
}
