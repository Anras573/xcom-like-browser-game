using System.Diagnostics;
using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical.Vision;

namespace Firewall.Rules.Tactical;

/// <summary>How difficulty bends the AI (GDD §7): noise blurs its choices and flank weight sharpens its tactics.</summary>
public sealed record AiDifficulty(double NoiseScale, double FlankScale)
{
    public static AiDifficulty Recruit { get; } = new(1.5, 0.75);
    public static AiDifficulty Normal { get; } = new(1, 1);
    public static AiDifficulty Veteran { get; } = new(0.5, 1.25);
}

/// <summary>How long one unit's turn took to plan, for the debug timing log.</summary>
public readonly record struct AiTiming(UnitId Unit, string Profile, int Steps, double Milliseconds);

/// <summary>
/// The default enemy brain (GDD §4.9). Each step it scores the tiles the unit can reach, then the
/// best few tiles' follow-up actions, runs the best <c>(move, action)</c> pair as regular commands
/// and plans again, because reaction fire and new sightings change the picture.
/// Only soldiers the enemy faction sees (and where it last saw others) count: it is never omniscient.
/// Deterministic: noise comes from forks of the state's RNG, labelled by turn, unit and step.
/// </summary>
public sealed class UtilityAi(
    GameData data,
    AiDifficulty? difficulty = null,
    Action<AiTiming>? timingLog = null
) : IAiController
{
    private const int MaxSteps = 6;
    private const int MaxCandidates = 60;
    private const int ActionTiles = 5;
    private const int DashShotHit = 20;

    private readonly AiDifficulty _difficulty = difficulty ?? AiDifficulty.Normal;

    public void TakeTurn(TacticalUnit unit, AiContext ctx)
    {
        if (
            !data.Enemies.TryGet(unit.DefId, out var def)
            || data.AiProfiles.FirstOrDefault(p => p.Kind == def.Behavior.Kind) is not { } profile
        )
            return;

        var clock = Stopwatch.StartNew();
        var steps = 0;
        while (steps < MaxSteps && CanAct(unit, ctx))
        {
            var rng = ctx.State.Rng.Fork($"ai:{ctx.State.TurnNumber}:{unit.Id.Value}:{steps++}");
            var plan = new Planner(data, _difficulty, def, profile, ctx, unit, rng).Plan();
            var acted = plan is not null && Run(plan, unit, ctx);
            if (!acted && !Fallback(unit, ctx))
                break;
        }
        timingLog?.Invoke(
            new AiTiming(unit.Id, profile.Id, steps, clock.Elapsed.TotalMilliseconds)
        );
    }

    private static bool CanAct(TacticalUnit unit, AiContext ctx) =>
        ctx.State.Result is null
        && unit is { IsAlive: true, Evacuated: false, ActionPoints: > 0 }
        && !unit.Statuses.Stunned
        && !unit.Statuses.IsDown;

    /// <summary>Executes the plan's move, then its action if it is still valid. True if any command ran.</summary>
    private static bool Run(Plan plan, TacticalUnit unit, AiContext ctx)
    {
        var ran = false;
        if (plan.MoveTo is { } dest)
        {
            var move = new MoveCommand(unit.Id, dest);
            if (!ctx.Validate(move).IsValid)
                return false;
            ctx.Execute(move);
            ran = true;
        }
        if (plan.Action is { } action && CanAct(unit, ctx))
        {
            var cmd = new UseAbilityCommand(unit.Id, action.AbilityId, action.Target);
            if (ctx.Validate(cmd).IsValid)
            {
                ctx.Execute(cmd);
                ran = true;
            }
        }
        return ran;
    }

    /// <summary>Nothing worth doing: overwatch, else hunker down, else give up.</summary>
    private static bool Fallback(TacticalUnit unit, AiContext ctx)
    {
        foreach (var id in new[] { AbilityRegistry.Overwatch, AbilityRegistry.HunkerDown })
        {
            var cmd = new UseAbilityCommand(unit.Id, id, Target.None);
            if (!ctx.Validate(cmd).IsValid)
                continue;
            ctx.Execute(cmd);
            return true;
        }
        return false;
    }

    private sealed record PlannedAction(string AbilityId, Target Target);

    private sealed record Plan(GridPos? MoveTo, PlannedAction? Action, double Score);

    private readonly record struct Threat(GridPos Pos, int Sight, UnitId Id);

    private readonly record struct Candidate(GridPos Tile, int ApCost, double Score);

    private readonly record struct ShotValue(double Value, int Hit);

    /// <summary>One planning step: knowledge, candidate tiles, tile scores, then actions for the best tiles.</summary>
    private sealed class Planner
    {
        private static readonly string[] ShotAbilities =
        [
            AbilityRegistry.Shoot,
            AbilityRegistry.Melee,
        ];

        private readonly GameData _data;
        private readonly EnemyDef _def;
        private readonly AiProfileDef _p;
        private readonly AiContext _ctx;
        private readonly TacticalState _state;
        private readonly TacticalUnit _unit;
        private readonly IRandom _rng;
        private readonly double _wCover,
            _wFlank,
            _wNoise;
        private readonly int _rangeMin,
            _rangeMax;

        private readonly List<TacticalUnit> _soldiers;
        private readonly List<Threat> _threats;
        private readonly List<GridPos> _approach;
        private readonly bool _searching;
        private MoveZones? _zones;
        private readonly List<TacticalUnit> _overwatchers;
        private readonly List<TacticalUnit> _allies;
        private readonly Dictionary<(GridPos, GridPos), bool> _los = [];

        public Planner(
            GameData data,
            AiDifficulty difficulty,
            EnemyDef def,
            AiProfileDef profile,
            AiContext ctx,
            TacticalUnit unit,
            IRandom rng
        )
        {
            _data = data;
            _def = def;
            _p = profile;
            _ctx = ctx;
            _state = ctx.State;
            _unit = unit;
            _rng = rng;
            _wCover = profile.CoverWeight * def.Behavior.CoverWeight;
            _wFlank = profile.FlankWeight * def.Behavior.FlankWeight * difficulty.FlankScale;
            _wNoise = profile.Noise * difficulty.NoiseScale;
            var (min, max) =
                def.Behavior.PreferredRangeMax > 0
                    ? (def.Behavior.PreferredRangeMin, def.Behavior.PreferredRangeMax)
                    : (profile.PreferredRangeMin, profile.PreferredRangeMax);
            (_rangeMin, _rangeMax) = (min, max);

            var vision = _state.EnemyVision;
            _soldiers = _state
                .Units.Where(u => u.Faction == Faction.Firewall && u.IsStanding)
                .Where(vision.IsUnitVisible)
                .ToList();
            _threats = _soldiers.Select(s => new Threat(s.Position, s.Sight, s.Id)).ToList();
            foreach (var (id, seen) in vision.LastKnownEnemies(_state.TurnNumber))
                if (_state.Unit(id) is { Faction: Faction.Firewall, IsStanding: true } lost)
                    _threats.Add(new Threat(seen.Position, lost.Sight, id));

            // Husks go for civilians within reach before soldiers.
            var civilians =
                profile.CivilianRadius > 0
                    ? _state
                        .Units.Where(u => u is { Faction: Faction.Neutral, IsStanding: true })
                        .Where(u => u.Position.DistanceTo(unit.Position) <= profile.CivilianRadius)
                        .ToList()
                    : [];
            _approach = (
                civilians.Count > 0
                    ? civilians.Select(c => c.Position)
                    : _threats.Select(t => t.Pos)
            ).ToList();
            Targets = [.. civilians, .. _soldiers];

            // Nobody known to hunt: sweep toward ground the enemy has not looked at yet, so an
            // active unit cannot sit in overwatch forever while the squad stays out of sight.
            if (_approach.Count == 0 && Targets.Count == 0 && !profile.HoldUnlessFlanked)
                if (SearchWaypoint() is { } waypoint)
                {
                    _approach.Add(waypoint);
                    _searching = true;
                }

            _overwatchers = _soldiers.Where(s => s.Statuses.Overwatch).ToList();
            _allies = _state
                .Units.Where(u =>
                    u.Faction == Faction.Meridian
                    && u.Id != unit.Id
                    && u.IsOnMap
                    && data.Enemies.TryGet(u.DefId, out var d)
                    && d.Robot
                )
                .ToList();
        }

        private List<TacticalUnit> Targets { get; }

        public Plan? Plan()
        {
            if (_approach.Count == 0 && Targets.Count == 0)
                return OverwatchPlan(_unit.Position, 0);

            var zones = Zones;
            var aps = _unit.ActionPoints;
            var candidates = Candidates(zones, aps);
            var best = (Plan?)null;
            void Consider(Plan plan)
            {
                if (best is null || plan.Score > best.Score)
                    best = plan;
            }

            // Tiles that cost AP to reach leave less to act with; dashes leave nothing.
            var bestShot = 0;
            var pairs = new List<(Candidate Tile, PlannedAction? Action, double Value, int Hit)>();
            foreach (var c in candidates.Take(ActionTiles).Where(c => c.ApCost < 2))
            {
                if (aps - c.ApCost < 1)
                {
                    pairs.Add((c, null, 0, 0));
                    continue;
                }
                var ghost = Ghost(c.Tile, c.ApCost > 0);
                var shot = BestShot(ghost);
                if (shot is { } s)
                {
                    bestShot = Math.Max(bestShot, s.Hit);
                    pairs.Add((c, s.Action, s.Value.Value, s.Value.Hit));
                }
                else
                    pairs.Add((c, null, 0, 0));
                if (c.ApCost == 0 && AbilityAction() is { } special)
                    pairs.Add((c, special.Action, special.Value, 0));
            }
            foreach (var c in candidates.Where(c => c.ApCost == 2))
                pairs.Add((c, null, 0, 0));

            foreach (var (tile, action, value, _) in pairs)
            {
                var move = tile.Tile == _unit.Position ? (GridPos?)null : tile.Tile;
                if (action is not null)
                    Consider(new Plan(move, action, tile.Score + value));
                else
                    // Staying put with nothing to do is a plan too: it lets the move cost decide.
                    Consider(new Plan(move, null, tile.Score));
            }

            // Overwatch, when nothing is worth shooting at.
            if (!_searching && _p.OverwatchBelowHit > 0 && bestShot < _p.OverwatchBelowHit)
                foreach (var (tile, _, _, _) in pairs.Where(p => p.Tile.ApCost < 2))
                    if (
                        _unit.ActionPoints - tile.ApCost >= 1
                        && OverwatchPlan(tile.Tile, tile.Score) is { } ow
                    )
                        Consider(ow);
            return best;
        }

        private MoveZones Zones => _zones ??= _ctx.MoveRange(_unit);

        /// <summary>
        /// The farthest tile this turn along the shortest path to the nearest tile the enemy faction
        /// has never seen (else the nearest one out of sight now).
        /// </summary>
        private GridPos? SearchWaypoint()
        {
            var vision = _state.EnemyVision;
            var map = _state.Map;
            var all = Pathfinder.Reachable(_state, _unit, map.Width * map.Height * 3);
            GridPos? target = null;
            var best = (Rank: int.MaxValue, Cost: int.MaxValue);
            foreach (var tile in all.Tiles())
            {
                var rank = vision[tile] switch
                {
                    TileVisibility.Unexplored => 0,
                    TileVisibility.Explored => 1,
                    _ => 2,
                };
                var cost = all.CostTo(tile)!.Value;
                if (rank < 2 && (rank, cost).CompareTo(best) < 0)
                {
                    best = (rank, cost);
                    target = tile;
                }
            }
            if (target is not { } goal || all.PathTo(goal) is not { } path)
                return null;
            return path.Skip(1).Where(Zones.Reachable.Contains).Cast<GridPos?>().LastOrDefault();
        }

        private Plan? OverwatchPlan(GridPos tile, double score) =>
            _p.OverwatchBelowHit > 0 && _unit.Abilities.Contains(AbilityRegistry.Overwatch)
                ? new Plan(
                    tile == _unit.Position ? null : tile,
                    new PlannedAction(AbilityRegistry.Overwatch, Target.None),
                    score + _p.OverwatchValue
                )
                : null;

        // ------------------------------------------------------------------ tiles

        /// <summary>The reachable tiles worth scoring, best first (the stay-put option included).</summary>
        private List<Candidate> Candidates(MoveZones zones, int aps)
        {
            var options = new List<(GridPos Tile, int Ap)> { (_unit.Position, 0) };
            var holding = _p.HoldUnlessFlanked && ExposedCount(_unit.Position) == 0;
            if (!holding)
            {
                options.AddRange(zones.Blue.Select(t => (t, 1)));
                if (aps >= 2 && (_p.AlwaysDash || !HasShotFromOneApTile(zones)))
                    options.AddRange(zones.Yellow.Select(t => (t, 2)));
            }

            // Cheap pre-score first: the LOS-based terms only run for the best few.
            var pre = options
                .Select(o => (o.Tile, o.Ap, Pre: PreScore(o.Tile, o.Ap)))
                .OrderByDescending(o => o.Pre)
                .Take(MaxCandidates)
                .ToList();
            return pre.Select(o => new Candidate(o.Tile, o.Ap, FullScore(o.Tile, o.Ap, zones)))
                .OrderByDescending(c => c.Score)
                .ToList();
        }

        private bool HasShotFromOneApTile(MoveZones zones)
        {
            if (_unit.ActionPoints < 2)
                return true;
            foreach (var tile in zones.Blue.Prepend(_unit.Position))
            {
                var ghost = Ghost(tile, tile != _unit.Position);
                foreach (var target in Targets)
                foreach (var ability in ShotAbilities)
                    if (
                        _unit.Abilities.Contains(ability)
                        && Preview(ghost, target, ability)
                            is { CanFire: true, HitChance: >= DashShotHit }
                    )
                        return true;
            }
            return false;
        }

        private double Distance(GridPos tile) =>
            _approach.Count == 0 ? 0 : _approach.Min(a => a.DistanceTo(tile));

        private double RangePenalty(GridPos tile)
        {
            if (_approach.Count == 0)
                return 0;
            var d = Distance(tile);
            if (_searching)
                return d;
            return Math.Max(0, Math.Max(_rangeMin - d, d - _rangeMax));
        }

        private double AllyProximity(GridPos tile) =>
            _p.AllyWeight == 0
                ? 0
                : Math.Min(
                    1,
                    _allies.Count(a => a.Position.DistanceTo(tile) <= _p.AllyRange) / 2.0
                );

        private double PreScore(GridPos tile, int ap)
        {
            var profile = CoverCalculator.GetProfile(_state.Map, tile);
            var cover = 0.0;
            foreach (var t in _threats)
                cover +=
                    CoverCalculator
                        .Evaluate(profile, t.Pos.X - tile.X, t.Pos.Y - tile.Y)
                        .DefenseBonus / (double)CoverCalculator.FullDefense;
            if (_threats.Count > 0)
                cover /= _threats.Count;
            return _wCover * cover
                - _p.RangeWeight * RangePenalty(tile)
                + _p.AllyWeight * AllyProximity(tile)
                - _p.MoveCost * ap;
        }

        private double FullScore(GridPos tile, int ap, MoveZones zones)
        {
            var profile = CoverCalculator.GetProfile(_state.Map, tile);
            double cover = 0;
            var seen = 0;
            var exposed = 0;
            foreach (var t in _threats)
            {
                if (!HasLos(t.Pos, tile, t.Sight))
                    continue;
                var result = CoverCalculator.Evaluate(profile, t.Pos.X - tile.X, t.Pos.Y - tile.Y);
                cover += result.DefenseBonus / (double)CoverCalculator.FullDefense;
                seen++;
                if (result.Flanked)
                    exposed++;
            }
            // Nobody who knows about the tile can see it: as good as full cover.
            cover = seen == 0 ? 1 : cover / seen;

            var flanks = Targets.Any(t =>
                t.Faction == Faction.Firewall
                && HasLos(tile, t.Position, _unit.Sight)
                && CoverCalculator.Evaluate(_state.Map, t.Position, tile).Flanked
            );
            var watchers = 0;
            if (ap > 0 && _overwatchers.Count > 0 && zones.Reachable.PathTo(tile) is { } path)
                watchers = _overwatchers.Count(o =>
                    path.Skip(1).Any(step => HasLos(o.Position, step, o.Sight))
                );

            return _wCover * cover
                + _wFlank * (flanks ? 1 : 0)
                - _p.ExposedWeight * exposed
                - _p.RangeWeight * RangePenalty(tile)
                + _p.AllyWeight * AllyProximity(tile)
                - _p.OverwatchAvoidWeight * watchers
                - _p.MoveCost * ap
                + _rng.NextDouble() * _wNoise;
        }

        private int ExposedCount(GridPos tile)
        {
            var profile = CoverCalculator.GetProfile(_state.Map, tile);
            return _threats.Count(t =>
                HasLos(t.Pos, tile, t.Sight)
                && CoverCalculator.Evaluate(profile, t.Pos.X - tile.X, t.Pos.Y - tile.Y).Flanked
            );
        }

        private bool HasLos(GridPos from, GridPos to, int sight)
        {
            if (!_los.TryGetValue((from, to), out var result))
                _los[(from, to)] = result = LosTracer.CanSee(_state.Map, from, sight, to);
            return result;
        }

        // ------------------------------------------------------------------ actions

        /// <summary>The unit as it would stand on <paramref name="tile"/>, for hit previews.</summary>
        private TacticalUnit Ghost(GridPos tile, bool moved)
        {
            var ghost = new TacticalUnit(_unit.Id, _unit.Faction, _unit.DefId, _unit.DisplayName)
            {
                Position = tile,
                Hp = _unit.Hp,
                Aim = _unit.Aim,
                Sight = _unit.Sight,
                Weapon = _unit.Weapon,
                MovedThisTurn = _unit.MovedThisTurn || moved,
            };
            ghost.Abilities.UnionWith(_unit.Abilities);
            ghost.Statuses.Flags = _unit.Statuses.Flags;
            return ghost;
        }

        private ShotPreview Preview(TacticalUnit ghost, TacticalUnit target, string ability) =>
            HitCalculator.Preview(
                _state,
                ghost,
                target,
                ShotContext.For(
                    _data,
                    ghost,
                    ability == AbilityRegistry.Melee ? RangeType.Melee : RangeType.Ranged
                )
            );

        private (PlannedAction Action, ShotValue Value, int Hit)? BestShot(TacticalUnit ghost)
        {
            (PlannedAction, ShotValue, int)? best = null;
            foreach (var ability in ShotAbilities.Where(_unit.Abilities.Contains))
            foreach (var target in Targets)
            {
                var preview = Preview(ghost, target, ability);
                if (!preview.CanFire || preview.HitChance == 0)
                    continue;
                var value = Value(preview, target);
                if (best is null || value.Value > best.Value.Item2.Value)
                    best = (
                        new PlannedAction(ability, Target.Unit(target.Id)),
                        value,
                        preview.HitChance
                    );
            }
            return best;
        }

        /// <summary>Expected damage plus a bonus for the chance to kill.</summary>
        private ShotValue Value(ShotPreview preview, TacticalUnit target)
        {
            var hit = preview.HitChance / 100.0;
            var crit = preview.CritChance / 100.0;
            var rolls = preview.DamageMax - preview.DamageMin + 1;
            double damage = 0,
                kill = 0;
            for (var r = preview.DamageMin; r <= preview.DamageMax; r++)
            {
                var plain = HitCalculator.Damage(r, false, target.Armor);
                var crits = HitCalculator.Damage(r, true, target.Armor);
                damage +=
                    (1 - crit) * Math.Min(plain, target.Hp) + crit * Math.Min(crits, target.Hp);
                kill +=
                    (1 - crit) * (plain >= target.Hp ? 1 : 0) + crit * (crits >= target.Hp ? 1 : 0);
            }
            var value = _p.DamageWeight * hit * damage / rolls + _p.KillBonus * hit * kill / rolls;
            return new ShotValue(value, preview.HitChance);
        }

        /// <summary>Suppression and Overclock: used from where the unit stands, and only if the engine allows them.</summary>
        private (PlannedAction Action, double Value)? AbilityAction()
        {
            (PlannedAction, double)? best = null;
            void Offer(string ability, TacticalUnit target, double value)
            {
                var action = new PlannedAction(ability, Target.Unit(target.Id));
                if (
                    (best is null || value > best.Value.Item2)
                    && _ctx.Validate(
                        new UseAbilityCommand(_unit.Id, ability, action.Target)
                    ).IsValid
                )
                    best = (action, value);
            }

            if (_p.SuppressWeight > 0 && _unit.Abilities.Contains("suppression"))
                foreach (var soldier in _soldiers)
                    Offer("suppression", soldier, _p.SuppressWeight * BestAllyHit(soldier) / 100.0);
            if (_p.SupportWeight > 0 && _unit.Abilities.Contains("proxy_overclock"))
                foreach (var ally in _allies.Where(a => a.ActionPoints > 0 && HasShot(a)))
                    Offer("proxy_overclock", ally, _p.SupportWeight);
            return best;
        }

        /// <summary>The best hit chance any other enemy has on <paramref name="soldier"/> from where it stands.</summary>
        private int BestAllyHit(TacticalUnit soldier) =>
            _state
                .Units.Where(u => u.Faction == Faction.Meridian && u.IsOnMap && u.Id != _unit.Id)
                .Select(a => HitCalculator.Preview(_state, a, soldier, ShotContext.For(_data, a)))
                .Where(p => p.CanFire)
                .Select(p => p.HitChance)
                .DefaultIfEmpty(0)
                .Max();

        private bool HasShot(TacticalUnit shooter) =>
            _soldiers.Any(s =>
                HitCalculator.Preview(_state, shooter, s, ShotContext.For(_data, shooter)).CanFire
            );
    }
}
