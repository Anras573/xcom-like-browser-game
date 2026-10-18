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

    public bool IsAlive { get; set; } = true;

    /// <summary>False for enemies in a pod that has not activated yet.</summary>
    public bool IsActive { get; set; } = true;

    public PodId? Pod { get; set; }

    /// <summary>Sprite set, such as <c>soldier1</c> or <c>zombie1</c>.</summary>
    public string AppearanceId { get; set; } = "";
}
