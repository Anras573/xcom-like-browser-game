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

/// <summary>A pod of the mission: its name and where it spawned.</summary>
public sealed record Pod(PodId Id, string Name, GridPos Spawn);

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
}

/// <summary>
/// A unit's status effects: on/off flags plus bleed-out, which counts the unit's own turns left
/// (0 = not bleeding out).
/// </summary>
public sealed class StatusSet
{
    public StatusFlags Flags { get; set; }

    public int BleedingOutTurnsLeft { get; set; }

    public bool IsBleedingOut => BleedingOutTurnsLeft > 0;

    public bool Has(StatusFlags flag) => (Flags & flag) == flag;

    public void Set(StatusFlags flag, bool on = true) => Flags = on ? Flags | flag : Flags & ~flag;

    public bool Overwatch => Has(StatusFlags.Overwatch);
    public bool Hunkered => Has(StatusFlags.Hunkered);
    public bool Suppressed => Has(StatusFlags.Suppressed);
    public bool Stunned => Has(StatusFlags.Stunned);
    public bool Stabilized => Has(StatusFlags.Stabilized);
    public bool Smoked => Has(StatusFlags.Smoked);
    public bool Capturable => Has(StatusFlags.Capturable);
}
