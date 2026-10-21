using Firewall.Rules.Data;
using Firewall.Rules.Tactical.Vision;

namespace Firewall.Rules.Tactical;

/// <summary>Pod patrol, activation and scamper (GDD §4.8).</summary>
public sealed partial class TacticalEngine
{
    /// <summary>How far from its spawn a pod's patrol target can be, in tiles.</summary>
    public const int PatrolRadius = 8;

    /// <summary>Chain activation reaches pods within this many tiles.</summary>
    public const int ChainRadius = 4;

    /// <summary>Mission parameter (<c>missions.json</c>): 1 turns chain activation on. Off by default.</summary>
    public const string ChainActivationParameter = "podChainActivation";

    /// <summary>Activates pods after each step: a member sees a soldier, or was shot at or caught in a blast.</summary>
    private sealed class PodActivationHook(TacticalEngine engine) : IStepHook
    {
        public StepResult OnUnitStepped(StepContext ctx)
        {
            var woken = engine.ActivatePodsFromEvents(ctx.Events, 0);
            woken.AddRange(engine.ActivatePodsThatSee(ctx.Events));
            // A patrolling pod that wakes mid-walk scampers instead of finishing the walk.
            return ctx.Mover.Pod is { } pod && woken.Contains(pod)
                ? StepResult.Interrupt
                : StepResult.Continue;
        }
    }

    private bool ChainActivation =>
        _data.Missions.TryGet(State.Objective.MissionId, out var mission)
        && mission.Parameters.GetValueOrDefault(ChainActivationParameter) > 0;

    private List<TacticalUnit> LivingMembers(Pod pod) =>
        pod.Members.Select(State.Unit).Where(u => u.IsStanding).ToList();

    // ------------------------------------------------------------------ activation

    /// <summary>Wakes every dormant pod with a member that sees a soldier; returns the pods woken.</summary>
    private List<PodId> ActivatePodsThatSee(List<GameEvent> events)
    {
        var woken = new List<PodId>();
        var soldiers = State
            .Units.Where(u => u.Faction == Faction.Firewall && u.IsStanding)
            .ToList();
        if (soldiers.Count == 0)
            return woken;
        foreach (var pod in State.Pods)
        {
            if (pod.Active || State.Result is not null)
                continue;
            if (
                LivingMembers(pod)
                    .Any(member => soldiers.Any(s => LosTracer.CanSee(State, member, s)))
            )
                woken.AddRange(ActivatePod(pod, events));
        }
        return woken;
    }

    /// <summary>Wakes the pods behind shots (hit or miss) and explosions in <paramref name="events"/> from <paramref name="from"/> on.</summary>
    private List<PodId> ActivatePodsFromEvents(List<GameEvent> events, int from)
    {
        var woken = new List<PodId>();
        // Snapshot: activating a pod appends to the list.
        var fresh = events.Skip(from).ToList();
        foreach (var e in fresh)
        {
            IEnumerable<Pod> hit = e switch
            {
                ShotResolved shot when State.Unit(shot.Target).Pod is { } id => [PodById(id)],
                ExplosionAt blast => State.Pods.Where(p =>
                    p.Members.Any(m =>
                        State.Unit(m) is { IsOnMap: true } unit
                        && LosTracer.InRange(blast.Pos, unit.Position, blast.Radius)
                    )
                ),
                _ => [],
            };
            foreach (var pod in hit.ToList())
                if (!pod.Active && State.Result is null)
                    woken.AddRange(ActivatePod(pod, events));
        }
        return woken;
    }

    private Pod PodById(PodId id) => State.Pods.First(p => p.Id == id);

    /// <summary>
    /// Wakes <paramref name="pod"/>: <see cref="PodActivated"/>, then every member's free scamper
    /// move. Returns the pods woken, this one first, then any chained to it.
    /// </summary>
    private List<PodId> ActivatePod(Pod pod, List<GameEvent> events)
    {
        pod.Active = true;
        var woken = new List<PodId> { pod.Id };
        var members = LivingMembers(pod).OrderBy(m => m.Id.Value).ToList();
        foreach (var member in members)
            member.IsActive = true;

        // Chain: pods close to this one wake as it does (decided before anyone scampers away).
        var chained = ChainActivation
            ? State
                .Pods.Where(other =>
                    !other.Active
                    && LivingMembers(other)
                        .Any(o =>
                            members.Any(m => LosTracer.InRange(m.Position, o.Position, ChainRadius))
                        )
                )
                .ToList()
            : [];

        // Nobody left to reveal (the shot that woke the pod killed its last member).
        if (members.Count == 0)
            return woken;
        events.Add(
            new PodActivated(
                pod.Id,
                pod.Name,
                members[0].Position,
                members.Select(m => m.Id).ToList()
            )
        );

        // The enemy side's view includes the dormant pods' eyes; the shot that woke them is known.
        State.EnemyVision.Recompute();
        foreach (var member in members)
        {
            if (!member.IsStanding || State.Result is not null)
                continue;
            if (PlanScamper(member) is { } path)
                Walk(member, path, segment => new UnitScampered(member.Id, segment), events);
        }

        foreach (var other in chained)
            if (!other.Active && State.Result is null)
                woken.AddRange(ActivatePod(other, events));
        return woken;
    }

    // ------------------------------------------------------------------ scamper

    /// <summary>
    /// The free move of one member: up to its Mobility, to the nearest tile with the best cover
    /// against the soldiers the enemy sees (all soldiers if none is in view); Husks charge the nearest
    /// soldier instead, and so does anyone who finds no cover in reach, but only half as far. Null
    /// when it should stay put. Stand-in until the AI tile scorer (#26).
    /// </summary>
    private IReadOnlyList<GridPos>? PlanScamper(TacticalUnit member)
    {
        var soldiers = State
            .Units.Where(u => u.Faction == Faction.Firewall && u.IsStanding)
            .ToList();
        if (soldiers.Count == 0)
            return null;
        var seen = soldiers.Where(State.EnemyVision.IsUnitVisible).ToList();
        var threats = seen.Count > 0 ? seen : soldiers;

        var reach = Pathfinder.Reachable(State, member, member.Mobility * 2);
        var tiles = reach.Tiles().Prepend(member.Position);
        var charger =
            _data.Enemies.TryGet(member.DefId, out var def) && def.Behavior.Kind == AiKind.Charger;

        var prey = soldiers.MinBy(s => Dist2(member.Position, s.Position))!.Position;
        int Cover(GridPos tile) =>
            threats.Sum(t => CoverCalculator.Evaluate(State.Map, tile, t.Position).DefenseBonus);

        GridPos best;
        if (charger)
            best = BestTile(reach, tiles, tile => -Dist2(tile, prey));
        else
        {
            best = BestTile(reach, tiles, Cover);
            // Nowhere to hide: close in a little rather than stand in the open.
            if (Cover(best) == 0)
                best = BestTile(
                    reach,
                    tiles.Where(t => reach.CostTo(t) <= member.Mobility),
                    tile => -Dist2(tile, prey)
                );
        }
        return best == member.Position ? null : reach.PathTo(best);
    }

    /// <summary>The tile with the highest score; ties go to the nearest, then lowest Y, then X.</summary>
    private static GridPos BestTile(
        ReachableTiles reach,
        IEnumerable<GridPos> tiles,
        Func<GridPos, int> score
    ) =>
        tiles
            .OrderByDescending(score)
            .ThenBy(t => reach.CostTo(t) ?? 0)
            .ThenBy(t => t.Y)
            .ThenBy(t => t.X)
            .First();

    private static int Dist2(GridPos a, GridPos b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    // ------------------------------------------------------------------ patrol

    private void PatrolPods(List<GameEvent> events)
    {
        foreach (var pod in State.Pods)
        {
            if (State.Result is not null)
                return;
            if (!pod.Active)
                PatrolPod(pod, events);
        }
    }

    // The leader walks toward the patrol target as far as its blue range allows; the others
    // regroup around it. Real moves: they run the step hooks, so overwatch and sight apply.
    private void PatrolPod(Pod pod, List<GameEvent> events)
    {
        var members = LivingMembers(pod);
        if (members.Count == 0)
            return;
        var leader = members[0];
        var rng = State.Rng.Fork($"patrol-{pod.Id.Value}-{State.TurnNumber}");

        if (leader.Position == pod.PatrolTarget)
            pod.PatrolTarget = RollPatrolTarget(pod, leader, rng);
        var path = PatrolPath(leader, pod.PatrolTarget);
        if (path is null)
        {
            // Boxed in or cut off from the target: pick another and try once more.
            pod.PatrolTarget = RollPatrolTarget(pod, leader, rng);
            path = PatrolPath(leader, pod.PatrolTarget);
        }
        if (path is not null)
            Walk(leader, path, segment => new UnitMoved(leader.Id, segment, false), events);
        if (pod.Active || State.Result is not null)
            return;
        if (leader.Position == pod.PatrolTarget)
            pod.PatrolTarget = RollPatrolTarget(pod, leader, rng);

        foreach (var follower in members.Skip(1))
        {
            if (!follower.IsStanding)
                continue;
            var reach = Pathfinder.Reachable(State, follower, follower.Mobility * 2);
            var near = BestTile(
                reach,
                reach.Tiles().Prepend(follower.Position),
                tile => -Dist2(tile, leader.Position)
            );
            if (near != follower.Position && reach.PathTo(near) is { } follow)
                Walk(
                    follower,
                    follow,
                    segment => new UnitMoved(follower.Id, segment, false),
                    events
                );
            if (pod.Active || State.Result is not null)
                return;
        }
    }

    // The reachable tile closest to the target, if it is closer than where the unit stands.
    private IReadOnlyList<GridPos>? PatrolPath(TacticalUnit unit, GridPos target)
    {
        var reach = Pathfinder.Reachable(State, unit, unit.Mobility * 2);
        var best = BestTile(reach, reach.Tiles().Prepend(unit.Position), t => -Dist2(t, target));
        return best == unit.Position ? null : reach.PathTo(best);
    }

    /// <summary>
    /// A random free walkable tile within <see cref="PatrolRadius"/> of the spawn. With
    /// <see cref="TacticalState.PodsDriftToSquad"/> it is the closest of three rolls to where the
    /// squad was last seen (or is, if the enemy has not seen it).
    /// </summary>
    private GridPos RollPatrolTarget(Pod pod, TacticalUnit leader, Rng.IRandom rng)
    {
        var candidates = new List<GridPos>();
        for (var y = pod.Spawn.Y - PatrolRadius; y <= pod.Spawn.Y + PatrolRadius; y++)
        for (var x = pod.Spawn.X - PatrolRadius; x <= pod.Spawn.X + PatrolRadius; x++)
        {
            var pos = new GridPos(x, y);
            if (
                State.Map.InBounds(pos)
                && State.Map.IsWalkable(pos)
                && pos != leader.Position
                && Dist2(pos, pod.Spawn) <= PatrolRadius * PatrolRadius
            )
                candidates.Add(pos);
        }
        if (candidates.Count == 0)
            return pod.Spawn;

        var pick = candidates[rng.Next(0, candidates.Count)];
        if (!State.PodsDriftToSquad || SquadCentroid() is not { } centroid)
            return pick;
        for (var i = 0; i < 2; i++)
        {
            var other = candidates[rng.Next(0, candidates.Count)];
            if (Dist2(other, centroid) < Dist2(pick, centroid))
                pick = other;
        }
        return pick;
    }

    private GridPos? SquadCentroid()
    {
        var known = State
            .EnemyVision.LastKnownEnemies(State.TurnNumber)
            .Select(kv => kv.Value.Position)
            .ToList();
        if (known.Count == 0)
            known = State
                .Units.Where(u => u.Faction == Faction.Firewall && u.IsStanding)
                .Select(u => u.Position)
                .ToList();
        return known.Count == 0
            ? null
            : new GridPos(
                (int)Math.Round(known.Average(p => p.X)),
                (int)Math.Round(known.Average(p => p.Y))
            );
    }
}
