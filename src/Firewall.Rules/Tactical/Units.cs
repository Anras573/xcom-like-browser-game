namespace Firewall.Rules.Tactical;

public enum Faction
{
    Firewall,
    Meridian,
    Neutral,
}

public enum TacticalPhase
{
    Player,
    Enemy,
    Environment,
}

/// <summary>Stable identity of a unit within one <see cref="TacticalState"/>.</summary>
public readonly record struct UnitId(int Value)
{
    public override string ToString() => $"u{Value}";
}

/// <summary>An enemy pod; members share it so they activate together.</summary>
public readonly record struct PodId(int Value)
{
    public override string ToString() => $"pod{Value}";
}

/// <summary>
/// A pod of the mission (GDD §4.8). It starts dormant and patrols as a group; the first member is
/// the leader. Once <see cref="Active"/> it acts like any other enemy.
/// </summary>
public sealed class Pod(PodId id, string name, GridPos spawn)
{
    public PodId Id { get; } = id;
    public string Name { get; } = name;
    public GridPos Spawn { get; } = spawn;

    /// <summary>Everyone who spawned with the pod, leader first; the dead stay listed.</summary>
    public List<UnitId> Members { get; } = [];

    public bool Active { get; set; }

    /// <summary>Where the leader is heading while the pod patrols; re-rolled on arrival.</summary>
    public GridPos PatrolTarget { get; set; } = spawn;
}

/// <summary>What a mission asks of <see cref="PodSpawner"/>: a pod's name and its members' enemy ids.</summary>
public sealed record PodSpec(string Name, IReadOnlyList<string> EnemyIds);

[Flags]
public enum StatusFlags
{
    None = 0,
    Overwatch = 1 << 0,
    Hunkered = 1 << 1,
    Suppressed = 1 << 2,
    Stunned = 1 << 3,
    Stabilized = 1 << 4,
    Smoked = 1 << 5,
    Capturable = 1 << 6,

    /// <summary>Takes extra damage from every source for the rest of the mission (Rupture).</summary>
    Ruptured = 1 << 7,
}

/// <summary>
/// A unit's status effects: on/off flags plus bleed-out, which counts the environment phases left
/// (0 = not bleeding out).
/// </summary>
public sealed class StatusSet
{
    public StatusFlags Flags { get; set; }

    public int BleedingOutTurnsLeft { get; set; }

    /// <summary>Who suppressed this unit; the suppression ends when that unit's next turn starts or it goes down.</summary>
    public UnitId? SuppressedBy { get; set; }

    public bool IsBleedingOut => BleedingOutTurnsLeft > 0;

    /// <summary>Bleeding out or stabilized: on the map, but out of the fight for this mission.</summary>
    public bool IsDown => IsBleedingOut || Stabilized;

    public bool Has(StatusFlags flag) => (Flags & flag) == flag;

    public void Set(StatusFlags flag, bool on = true) => Flags = on ? Flags | flag : Flags & ~flag;

    public bool Overwatch => Has(StatusFlags.Overwatch);
    public bool Hunkered => Has(StatusFlags.Hunkered);
    public bool Suppressed => Has(StatusFlags.Suppressed);
    public bool Stunned => Has(StatusFlags.Stunned);
    public bool Stabilized => Has(StatusFlags.Stabilized);
    public bool Smoked => Has(StatusFlags.Smoked);
    public bool Capturable => Has(StatusFlags.Capturable);
    public bool Ruptured => Has(StatusFlags.Ruptured);
}

/// <summary>
/// Per-unit bookkeeping for soldier abilities (#30): what has been used this turn or phase and
/// what is armed. See <see cref="SoldierPerks"/>.
/// </summary>
public sealed class PerkState
{
    /// <summary>The unit dashed (spent 2 AP on one move) this turn; Run &amp; Gun needs it.</summary>
    public bool DashedThisTurn { get; set; }

    /// <summary>AP among <see cref="TacticalUnit.ActionPoints"/> that only a shot may spend (Run &amp; Gun).</summary>
    public int ShootOnlyAp { get; set; }

    public bool HitAndRunUsed { get; set; }

    /// <summary>A stun cost the unit this turn: it has no AP and is not "done" for Overclock to refill.</summary>
    public bool LostTurnToStun { get; set; }

    /// <summary>Kills refunded this turn by In The Zone.</summary>
    public int InTheZoneUses { get; set; }

    /// <summary>The first reaction shot of this phase against the unit was already absorbed.</summary>
    public bool LightningReflexesSpent { get; set; }

    /// <summary>The unit killed something and its next incoming attack misses.</summary>
    public bool UntouchableArmed { get; set; }

    /// <summary>Uses left per ability this mission, created on first use (see <see cref="Data.AbilityDef.Charges"/>).</summary>
    public Dictionary<string, int> ChargesUsed { get; } = new(StringComparer.Ordinal);
}
