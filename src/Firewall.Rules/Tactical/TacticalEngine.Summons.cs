using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

/// <summary>Summoning (GDD §5.6): the Relay Node's periodic Husks and the Cradle Guardian's Wardens.</summary>
public sealed partial class TacticalEngine
{
    /// <summary>Whatever should follow a change in HP or standing: suppression ends, the Guardian summons.</summary>
    private void Settle(List<GameEvent> events)
    {
        Suppression.Sweep(State, events);
        ReleaseCarried(events);
        foreach (var unit in State.Units.ToList())
        {
            if (
                !unit.IsAlive
                || unit.HasSummoned
                || !_data.Enemies.TryGet(unit.DefId, out var def)
                || def.Summon is not { BelowHpPercent: > 0 } summon
                || unit.Hp * 100 > summon.BelowHpPercent * unit.MaxHp
            )
                continue;
            unit.HasSummoned = true;
            Summon(unit, summon, events);
        }
    }

    /// <summary>
    /// A carrier who died or went down puts their load on a free tile next to them; a carried unit
    /// that died is let go the same way.
    /// </summary>
    private void ReleaseCarried(List<GameEvent> events)
    {
        foreach (var carrier in State.Units.Where(u => u.Carrying is not null).ToList())
        {
            var carried = carrier.Carrying!;
            if (carrier.IsStanding && carried.IsAlive)
                continue;
            var at = DropAbility.DropTile(State, carrier.Position) ?? carrier.Position;
            CarryAbility.Release(
                carrier,
                at,
                CarryAbility.Penalty(_data.Abilities[AbilityRegistry.Carry]),
                events
            );
        }
    }

    /// <summary>Every <see cref="SummonDef.EveryTurns"/>th environment phase each living relay spawns its units.</summary>
    private void RunRelays(List<GameEvent> events)
    {
        foreach (var unit in State.Units.ToList())
        {
            if (
                !unit.IsAlive
                || !_data.Enemies.TryGet(unit.DefId, out var def)
                || def.Summon is not { EveryTurns: > 0 } summon
                || State.TurnNumber % summon.EveryTurns != 0
            )
                continue;
            Summon(unit, summon, events);
        }
    }

    private static int Chebyshev(GridPos a, GridPos b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void Summon(TacticalUnit summoner, SummonDef summon, List<GameEvent> events)
    {
        var tier = EliteTransform.TierAt(_data.Elites, State.Convergence);
        var baseDef = _data.Enemies[summon.EnemyId];
        var def =
            tier > 0 && EliteTransform.HasVariants(baseDef)
                ? _data.Enemies[EliteTransform.VariantId(baseDef.Id, _data.Elites[tier - 1])]
                : baseDef;

        var radius = Math.Max(1, summon.Radius);
        var free = State
            .NearestFreeTiles(summoner.Position)
            .Where(t => Chebyshev(t, summoner.Position) <= radius)
            .ToList();
        var alive = State.Units.Count(u => u.IsAlive && u.SummonedBy == summoner.Id);
        for (var i = 0; i < summon.Count && i < free.Count; i++)
        {
            if (summon.MaxAlive > 0 && alive >= summon.MaxAlive)
                break;
            var letter = (char)('A' + State.Units.Count(u => u.DefId == def.Id) % 26);
            var unit = PodSpawner.CreateUnit(State, def, $"{def.Name} {letter}");
            unit.Position = free[i];
            unit.SummonedBy = summoner.Id;
            if (summoner.Pod is { } podId)
            {
                unit.Pod = podId;
                State.Pods.First(p => p.Id == podId).Members.Add(unit.Id);
            }
            State.AddUnit(unit);
            alive++;
            events.Add(
                new UnitSpawned(
                    unit.Id,
                    def.Id,
                    unit.AppearanceId,
                    def.Robot,
                    unit.Position,
                    unit.Hp,
                    unit.Armor,
                    summoner.Id
                )
            );
        }
    }

    /// <summary>
    /// Rescue: a civilian becomes controllable (2 AP, move and evac) once a soldier ends a move
    /// next to them. Civilians are not in a pod; they just stand until reached.
    /// </summary>
    private void RecruitCivilians(List<GameEvent> events)
    {
        if (State.Phase != TacticalPhase.Player)
            return;
        var soldiers = State.Units.Where(IsOperationalSoldier).ToList();
        foreach (
            var civilian in State
                .Units.Where(u =>
                    Civilians.IsCivilian(u) && u is { Controllable: false, IsStanding: true }
                )
                .ToList()
        )
        {
            if (!soldiers.Exists(s => Chebyshev(s.Position, civilian.Position) <= 1))
                continue;
            civilian.Controllable = true;
            civilian.ActionPoints = ApPerTurn;
            events.Add(new CivilianJoined(civilian.Id));
            events.Add(new ApChanged(civilian.Id, civilian.ActionPoints));
        }
    }
}
