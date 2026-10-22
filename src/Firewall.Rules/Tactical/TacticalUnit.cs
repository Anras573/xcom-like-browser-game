using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

/// <summary>A unit's weapon and the rounds left in the clip.</summary>
public sealed class WeaponState(WeaponDef def, int ammo)
{
    public WeaponDef Def { get; } = def;
    public int Ammo { get; set; } = ammo;
}

/// <summary>A utility item and its charges left this mission.</summary>
public sealed class ItemState(string itemId, int charges)
{
    public string ItemId { get; } = itemId;
    public int Charges { get; set; } = charges;
}

/// <summary>What a unit did this mission, for <see cref="MissionResult"/>.</summary>
public sealed class CombatStats
{
    public int Kills { get; set; }

    /// <summary>HP taken off targets; overkill does not count.</summary>
    public int DamageDealt { get; set; }
    public int ShotsFired { get; set; }
    public int Hits { get; set; }
}

/// <summary>
/// A soldier, enemy or neutral on the tactical map. Plain rules state (not ECS): presentation
/// reads it and never writes it.
/// </summary>
public sealed class TacticalUnit(UnitId id, Faction faction, string defId, string displayName)
{
    public UnitId Id { get; } = id;
    public Faction Faction { get; } = faction;

    /// <summary>The soldier template (<see cref="SquadDeployer.SoldierDefId"/>) or an <see cref="EnemyDef"/> id.</summary>
    public string DefId { get; } = defId;

    public string DisplayName { get; set; } = displayName;

    /// <summary>The soldier's rank (bleed-out chance); Rookie for enemies.</summary>
    public Rank Rank { get; set; }
    public GridPos Position { get; set; }

    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Aim { get; set; }
    public int Mobility { get; set; }
    public int Defense { get; set; }
    public int Armor { get; set; }
    public int Sight { get; set; }

    public int ActionPoints { get; set; }
    public bool MovedThisTurn { get; set; }

    /// <summary>Null for enemies, which attack from their <see cref="EnemyDef"/> damage range.</summary>
    public WeaponState? Weapon { get; set; }

    public List<ItemState> Items { get; } = [];
    public HashSet<string> Abilities { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Cooldowns { get; } = new(StringComparer.Ordinal);
    public StatusSet Statuses { get; } = new();

    /// <summary>Overwatch and the like: reaction shots waiting for a hostile to move (<see cref="ReactionFireHook"/>).</summary>
    public List<ReactionTrigger> Reactions { get; } = [];

    public CombatStats Stats { get; } = new();

    public bool IsAlive { get; set; } = true;

    /// <summary>Left the map through the evac zone; still alive, but no longer on the map.</summary>
    public bool Evacuated { get; set; }

    /// <summary>Alive and still on the map: it occupies its tile, can be seen and can be shot.</summary>
    public bool IsOnMap => IsAlive && !Evacuated;

    /// <summary>On the map and not down: still in the fight.</summary>
    public bool IsStanding => IsOnMap && !Statuses.IsDown;

    /// <summary>False for enemies in a pod that has not activated yet.</summary>
    public bool IsActive { get; set; } = true;

    public PodId? Pod { get; set; }

    /// <summary>The unit that summoned this one (Relay Node, Cradle Guardian); counts against the summoner's cap.</summary>
    public UnitId? SummonedBy { get; set; }

    /// <summary>The HP-triggered summon (Cradle Guardian) already happened.</summary>
    public bool HasSummoned { get; set; }

    /// <summary>Cannot be stunned or hacked.</summary>
    public bool ImmuneToStun { get; set; }

    /// <summary>Sprite set, such as <c>soldier1</c> or <c>zombie1</c>.</summary>
    public string AppearanceId { get; set; } = "";
}
