using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

/// <summary>
/// One soldier to build (#30): class, rank, the ability picked at each rank and the gear. The
/// debug squad builder fills these in; the barracks will later, from the roster.
/// </summary>
public sealed record SoldierSpec
{
    /// <summary>Null for a Rookie, who has no class.</summary>
    public string? ClassId { get; init; }
    public Rank Rank { get; init; }

    /// <summary>The ability chosen at each rank that offers a choice, in rank order (Sergeant, Lieutenant, Captain).</summary>
    public IReadOnlyList<string> Picks { get; init; } = [];

    /// <summary>1 to 3 (Ballistic, Coil, Arc); the weapon class comes from the class.</summary>
    public int WeaponTier { get; init; } = 1;
    public string ArmorId { get; init; } = "tac_vest";
    public IReadOnlyList<string> ItemIds { get; init; } = [];

    /// <summary>The first picks of the class up to <paramref name="rank"/>, a legal spec to start from.</summary>
    public static SoldierSpec Default(GameData data, string? classId, Rank rank) =>
        new()
        {
            ClassId = classId,
            Rank = classId is null ? Rank.Rookie : rank,
            Picks = classId is null
                ? []
                : SoldierBuilder
                    .PickRanks(data.Classes[classId], rank)
                    .Select(r => r.Abilities[0])
                    .ToList(),
        };
}

/// <summary>Turns a <see cref="SoldierSpec"/> into a unit with the stats of GDD §5.1 to §5.5.</summary>
public static class SoldierBuilder
{
    /// <summary>The ranks up to <paramref name="rank"/> at which <paramref name="def"/> offers a choice.</summary>
    public static IEnumerable<ClassRankDef> PickRanks(ClassDef def, Rank rank) =>
        def.AbilityTree.Where(r => r.Pick > 0 && r.Rank <= rank).OrderBy(r => r.Rank);

    /// <summary>Utility slots: one, and a second for a class that earns it by rank (the Tech at Sergeant).</summary>
    public static int ItemSlots(ClassDef? def, Rank rank) =>
        def?.ExtraUtilitySlotRank is { } extra && rank >= extra ? 2 : 1;

    /// <summary>
    /// What is wrong with <paramref name="spec"/>; empty when it is legal. A Rookie has no class, a
    /// higher rank needs one, and each choice must be one of that rank's abilities.
    /// </summary>
    public static IReadOnlyList<string> Validate(GameData data, SoldierSpec spec)
    {
        var errors = new List<string>();
        ClassDef? def = null;
        if (spec.ClassId is null)
        {
            if (spec.Rank != Rank.Rookie)
                errors.Add("Only a Rookie has no class.");
            if (spec.Picks.Count > 0)
                errors.Add("A Rookie has no abilities to pick.");
        }
        else if (!data.Classes.TryGet(spec.ClassId, out def))
            errors.Add($"Unknown class '{spec.ClassId}'.");
        else
        {
            if (spec.Rank == Rank.Rookie)
                errors.Add("A Rookie has no class.");
            var ranks = PickRanks(def, spec.Rank).ToList();
            if (spec.Picks.Count != ranks.Count)
                errors.Add($"{def.Name} needs {ranks.Count} ability picks at {spec.Rank}.");
            else
                for (var i = 0; i < ranks.Count; i++)
                    if (!ranks[i].Abilities.Contains(spec.Picks[i]))
                        errors.Add(
                            $"'{spec.Picks[i]}' is not a {ranks[i].Rank} ability of {def.Name}."
                        );
        }

        if (spec.WeaponTier is < 1 or > 3)
            errors.Add($"Weapon tier {spec.WeaponTier} must be 1 to 3.");
        if (!data.Armor.Contains(spec.ArmorId))
            errors.Add($"Unknown armor '{spec.ArmorId}'.");
        foreach (var id in spec.ItemIds)
            if (!data.Items.Contains(id))
                errors.Add($"Unknown item '{id}'.");
        if (spec.ItemIds.Count > ItemSlots(def, spec.Rank))
            errors.Add($"Too many items ({spec.ItemIds.Count}) for the utility slots.");
        return errors;
    }

    /// <summary>The class weapon lock: Breacher shotgun, Marksman sniper, Gunner LMG, Tech and Rookie rifle.</summary>
    public static WeaponClass WeaponClassOf(GameData data, string? classId) =>
        classId is null ? data.Soldier.StartingWeaponClass : data.Classes[classId].WeaponClass;

    /// <summary>The abilities a soldier of this spec has: the standard actions, the debug Stabilize, the automatic class ability and the picks.</summary>
    public static IEnumerable<string> AbilitiesOf(GameData data, SoldierSpec spec)
    {
        foreach (var id in AbilityRegistry.StandardActions)
            yield return id;
        yield return AbilityRegistry.DebugStabilize;
        if (spec.ClassId is null)
            yield break;
        foreach (var tier in data.Classes[spec.ClassId].AbilityTree)
            if (tier is { Pick: 0 } && tier.Rank <= spec.Rank)
                foreach (var id in tier.Abilities)
                    yield return id;
        foreach (var id in spec.Picks)
            yield return id;
    }

    /// <summary>Builds the unit. Throws when <see cref="Validate"/> finds a problem.</summary>
    public static TacticalUnit Build(
        TacticalState state,
        GameData data,
        SoldierSpec spec,
        string name,
        string appearanceId
    )
    {
        if (Validate(data, spec) is { Count: > 0 } errors)
            throw new ArgumentException(string.Join(" ", errors), nameof(spec));
        var soldier = data.Soldier;
        var armor = data.Armor[spec.ArmorId];
        var weapon = data.Weapon(WeaponClassOf(data, spec.ClassId), spec.WeaponTier);
        var hp = soldier.Hp + soldier.HpBonusRanks.Count(r => r <= spec.Rank) + armor.Hp;
        var unit = new TacticalUnit(
            state.NextUnitId(),
            Faction.Firewall,
            SquadDeployer.SoldierDefId,
            name
        )
        {
            Rank = spec.Rank,
            Hp = hp,
            MaxHp = hp,
            Aim = soldier.Aim + soldier.AimPerRank * (int)spec.Rank,
            Defense = soldier.Defense,
            Armor = armor.Armor,
            Sight = soldier.Sight,
            ActionPoints = TacticalEngine.ApPerTurn,
            Weapon = new WeaponState(weapon, weapon.Clip),
            AppearanceId = appearanceId,
        };
        unit.Abilities.UnionWith(AbilitiesOf(data, spec));
        unit.Mobility =
            soldier.Mobility + armor.Mobility + SoldierPerks.MobilityBonus(data, unit.Abilities);
        foreach (var id in spec.ItemIds)
        {
            var item = data.Items[id];
            // A passive item (the scope) is always on: its aim joins the soldier's.
            if (item.Kind == ItemKind.Passive)
                unit.Aim += (int)item.Parameters.GetValueOrDefault("aim");
            unit.Items.Add(new ItemState(id, SoldierPerks.ItemCharges(data, unit.Abilities, item)));
        }
        return unit;
    }
}
