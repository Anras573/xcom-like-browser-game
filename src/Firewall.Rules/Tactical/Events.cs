using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

/// <summary>
/// What happened, in order. Every event carries enough data to animate without reading the
/// state, which has already moved on by the time presentation plays it back.
/// </summary>
public abstract record GameEvent;

// Turn flow
public sealed record PhaseStarted(TacticalPhase Phase, int Turn) : GameEvent;

public sealed record ApChanged(UnitId Unit, int Ap) : GameEvent;

public sealed record AbilityUsed(UnitId Unit, string AbilityId, Target Target) : GameEvent;

/// <summary>Always the last event of the mission; carries everything the result screen shows.</summary>
public sealed record MissionEnded(MissionResult Result) : GameEvent;

/// <summary>Every soldier is out of AP; presentation ends the turn after a short delay.</summary>
public sealed record AllSoldiersExhausted : GameEvent;

// Movement
public sealed record UnitMoved(UnitId Unit, IReadOnlyList<GridPos> Path, bool Dashed) : GameEvent;

/// <summary>
/// A move ended early because the next tile held a unit the mover could not see when it planned the
/// route. The mover stays at <paramref name="At"/>; the AP is spent as planned.
/// </summary>
public sealed record MoveBlocked(UnitId Mover, GridPos At, UnitId Blocker) : GameEvent;

public sealed record UnitFacing(UnitId Unit, GridPos LookAt) : GameEvent;

// Combat
public sealed record ShotResolved(
    UnitId Attacker,
    UnitId Target,
    int HitChance,
    bool Hit,
    bool Crit,
    int Damage
) : GameEvent;

public sealed record UnitDamaged(UnitId Unit, int Amount, int HpAfter, int ArmorAfter) : GameEvent;

public sealed record UnitKilled(UnitId Unit) : GameEvent;

public sealed record UnitBleedingOut(UnitId Unit, int Turns) : GameEvent;

public sealed record UnitStabilized(UnitId Unit) : GameEvent;

/// <summary>The unit left the map through the evac zone.</summary>
public sealed record UnitEvacuated(UnitId Unit) : GameEvent;

public sealed record ReactionShotTriggered(UnitId Shooter, UnitId Mover) : GameEvent;

// Equipment and status
public sealed record AmmoChanged(UnitId Unit, int Ammo) : GameEvent;

public sealed record StatusAdded(UnitId Unit, StatusFlags Status) : GameEvent;

public sealed record StatusRemoved(UnitId Unit, StatusFlags Status) : GameEvent;

// Pods and map
/// <summary>
/// A dormant pod woke up. <paramref name="At"/> is where its leader stood, for the camera; the
/// scamper moves follow as <see cref="UnitScampered"/>.
/// </summary>
public sealed record PodActivated(PodId Pod, string Name, GridPos At, IReadOnlyList<UnitId> Members)
    : GameEvent;

public sealed record UnitScampered(UnitId Unit, IReadOnlyList<GridPos> Path) : GameEvent;

public sealed record TileChanged(GridPos Pos, TileType From, TileType To) : GameEvent;

public sealed record ExplosionAt(GridPos Pos, int Radius) : GameEvent;

public sealed record VisibilityChanged(
    IReadOnlyList<UnitId> NewlyVisibleEnemies,
    IReadOnlyList<UnitId> NewlyHiddenEnemies
) : GameEvent;
