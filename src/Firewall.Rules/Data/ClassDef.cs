using System.Text.Json.Serialization;

namespace Firewall.Rules.Data;

/// <summary>Abilities offered at one rank.</summary>
public sealed record ClassRankDef
{
    [JsonRequired]
    public Rank Rank { get; init; }

    /// <summary>How many of <see cref="Abilities"/> the player picks; 0 grants all automatically.</summary>
    [JsonRequired]
    public int Pick { get; init; }

    private readonly IReadOnlyList<string>? _abilities;

    [JsonRequired]
    public IReadOnlyList<string> Abilities
    {
        get => _abilities ?? [];
        init => _abilities = value;
    }
}

public sealed record ClassDef : IDef
{
    [JsonRequired]
    public string Id { get; init; } = "";

    [JsonRequired]
    public string Name { get; init; } = "";

    [JsonRequired]
    public string Role { get; init; } = "";

    [JsonRequired]
    public WeaponClass WeaponClass { get; init; }

    /// <summary>Character sprite pose while armed with the class weapon.</summary>
    [JsonRequired]
    public string Pose { get; init; } = "";

    /// <summary>Rank at which the class gains a 2nd utility slot, if any.</summary>
    public Rank? ExtraUtilitySlotRank { get; init; }

    private readonly IReadOnlyList<ClassRankDef>? _abilityTree;

    [JsonRequired]
    public IReadOnlyList<ClassRankDef> AbilityTree
    {
        get => _abilityTree ?? [];
        init => _abilityTree = value;
    }
}

/// <summary>XP needed for a rank.</summary>
public sealed record RankXp
{
    [JsonRequired]
    public Rank Rank { get; init; }

    [JsonRequired]
    public int Xp { get; init; }
}

/// <summary>Soldier base stats and progression (GDD §5.1). Exactly one exists.</summary>
public sealed record SoldierDef
{
    [JsonRequired]
    public int Hp { get; init; }

    private readonly IReadOnlyList<Rank>? _hpBonusRanks;

    [JsonRequired]
    public IReadOnlyList<Rank> HpBonusRanks
    {
        get => _hpBonusRanks ?? [];
        init => _hpBonusRanks = value;
    }

    [JsonRequired]
    public int Aim { get; init; }

    [JsonRequired]
    public int AimPerRank { get; init; }

    [JsonRequired]
    public int Mobility { get; init; }

    [JsonRequired]
    public int Defense { get; init; }

    [JsonRequired]
    public int Sight { get; init; }

    private readonly IReadOnlyList<RankXp>? _ranks;

    [JsonRequired]
    public IReadOnlyList<RankXp> Ranks
    {
        get => _ranks ?? [];
        init => _ranks = value;
    }

    [JsonRequired]
    public WeaponClass StartingWeaponClass { get; init; }

    private readonly IReadOnlyList<string>? _appearanceSets;

    [JsonRequired]
    public IReadOnlyList<string> AppearanceSets
    {
        get => _appearanceSets ?? [];
        init => _appearanceSets = value;
    }
}
