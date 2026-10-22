using Firewall.Rules.Data;

namespace Firewall.Rules.Tactical;

/// <summary>Summoning (GDD §5.6): the Relay Node's periodic Husks and the Cradle Guardian's Wardens.</summary>
public sealed partial class TacticalEngine
{
    /// <summary>Whatever should follow a change in HP or standing: suppression ends, the Guardian summons.</summary>
    private void Settle(List<GameEvent> events)
    {
        Suppression.Sweep(State, events);
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
}
