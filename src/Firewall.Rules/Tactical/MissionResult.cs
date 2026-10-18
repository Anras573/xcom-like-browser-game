using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

public enum MissionOutcome
{
    Success,
    Failure,

    /// <summary>Abort Mission: the squad on the evac zone left, everyone else was left behind.</summary>
    Aborted,
}

public enum SoldierFate
{
    Healthy,

    /// <summary>Survived with HP lost (<see cref="SoldierResult.HpLost"/>).</summary>
    Wounded,
    Dead,

    /// <summary>Left behind on the map; counts as dead.</summary>
    Mia,
}

/// <summary>One soldier's fate and record for the mission.</summary>
public sealed record SoldierResult(
    UnitId Unit,
    string Name,
    SoldierFate Status,
    int HpLost,
    int Kills,
    int DamageDealt,
    int ShotsFired,
    int Hits,
    bool Recovered
)
{
    public bool Survived => Status is SoldierFate.Healthy or SoldierFate.Wounded;
}

/// <summary>Resources brought home (GDD §5.7).</summary>
public sealed record Loot(int Credits, int Salvage, int Cores)
{
    public static Loot Empty { get; } = new(0, 0, 0);

    public Loot Add(Resource resource, int amount) =>
        resource switch
        {
            Resource.Credits => this with { Credits = Credits + amount },
            Resource.Salvage => this with { Salvage = Salvage + amount },
            _ => this with { Cores = Cores + amount },
        };
}

/// <summary>
/// How a mission ended: the outcome, every soldier's fate, kills and loot. Built once by the engine
/// when the mission ends (<see cref="TacticalState.Result"/>, <see cref="MissionEnded"/>).
/// </summary>
public sealed record MissionResult(
    MissionOutcome Outcome,
    int TurnsTaken,
    IReadOnlyList<SoldierResult> Soldiers,
    IReadOnlyDictionary<string, int> EnemiesKilled,
    Loot LootCollected,
    IReadOnlyList<string> Captured,
    UnitId? Mvp
)
{
    /// <summary>
    /// Reads the final state. Soldiers who evacuated survived. On success everyone still on the
    /// map is recovered: the downed count as wounded, the killed as recovered bodies. On failure the
    /// downed die and anyone else left on the map is MIA; on an abort everyone left behind is MIA.
    /// Loot rolls on its own stream and only on success.
    /// </summary>
    public static MissionResult Build(TacticalState state, GameData data, MissionOutcome outcome)
    {
        var soldiers = state
            .Units.Where(u => u.Faction == Faction.Firewall)
            .Select(u => SoldierResultOf(u, outcome))
            .ToList();

        var killed = state.Units.Where(u => u is { Faction: Faction.Meridian, IsAlive: false });
        var enemiesKilled = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var u in killed)
            enemiesKilled[u.DefId] = enemiesKilled.GetValueOrDefault(u.DefId) + 1;

        var loot = Loot.Empty;
        if (outcome == MissionOutcome.Success)
        {
            var rng = state.Rng.Fork("loot");
            foreach (var u in killed)
            foreach (var entry in data.Enemies[u.DefId].Loot)
                if (entry.ChancePercent >= 100 || rng.Roll(entry.ChancePercent))
                    loot = loot.Add(entry.Resource, entry.Amount);
        }

        // Most damage dealt; ties go to kills, then the lower id. Nobody dealt damage: no MVP.
        var mvp = soldiers
            .Where(s => s.DamageDealt > 0)
            .OrderByDescending(s => s.DamageDealt)
            .ThenByDescending(s => s.Kills)
            .ThenBy(s => s.Unit.Value)
            .Select(s => (UnitId?)s.Unit)
            .FirstOrDefault();

        return new MissionResult(
            outcome,
            state.TurnNumber,
            soldiers,
            enemiesKilled,
            loot,
            // Capturing Proxies comes with the Arc Stunner (#31).
            [],
            mvp
        );
    }

    private static SoldierResult SoldierResultOf(TacticalUnit u, MissionOutcome outcome)
    {
        var hpLost = u.MaxHp - u.Hp;
        var survived = hpLost > 0 || u.Statuses.IsDown ? SoldierFate.Wounded : SoldierFate.Healthy;
        var (fate, recovered) = u switch
        {
            { Evacuated: true } => (survived, true),
            { IsAlive: false } => (SoldierFate.Dead, outcome == MissionOutcome.Success),
            _ when outcome == MissionOutcome.Success => (survived, true),
            _ when outcome == MissionOutcome.Failure && u.Statuses.IsDown => (
                SoldierFate.Dead,
                false
            ),
            _ => (SoldierFate.Mia, false),
        };
        return new SoldierResult(
            u.Id,
            u.DisplayName,
            fate,
            fate == SoldierFate.Wounded ? hpLost : 0,
            u.Stats.Kills,
            u.Stats.DamageDealt,
            u.Stats.ShotsFired,
            u.Stats.Hits,
            recovered
        );
    }
}
