using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;

namespace Firewall.Rules.Tests;

/// <summary>The utility AI (#26), on small ASCII fixtures. Rows are listed top first.</summary>
public class UtilityAiTests
{
    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", f))
    );

    internal static TacticalState Field(ulong seed, params string[] rows) =>
        new(
            MapParser.Parse(
                new MapFile
                {
                    Id = "t",
                    Name = "T",
                    Biome = Biome.Industrial,
                    Rows = rows.ToList(),
                },
                Data.Tiles
            ),
            new Pcg32Random(seed)
        );

    internal static string[] Open(int width, int height) =>
        Enumerable.Range(0, height).Select(_ => new string('.', width)).ToArray();

    internal static TacticalUnit Soldier(TacticalState state, int x, int y, int hp = 10)
    {
        var weapon = Data.Weapons["rifle_t1"];
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Firewall, "soldier", "Soldier")
        {
            Position = new GridPos(x, y),
            Hp = hp,
            MaxHp = hp,
            Aim = 65,
            Mobility = 6,
            Sight = 12,
            ActionPoints = 2,
            Weapon = new WeaponState(weapon, weapon.Clip),
        };
        unit.Abilities.UnionWith(AbilityRegistry.StandardActions);
        state.AddUnit(unit);
        return unit;
    }

    internal static TacticalUnit Enemy(TacticalState state, string defId, int x, int y)
    {
        var def = Data.Enemies[defId];
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Meridian, def.Id, def.Name)
        {
            Position = new GridPos(x, y),
            Hp = def.Hp,
            MaxHp = def.Hp,
            Aim = def.Aim,
            Mobility = def.Mobility,
            Defense = def.Defense,
            Armor = def.Armor,
            Sight = def.Sight,
        };
        unit.Abilities.UnionWith(def.Abilities);
        state.AddUnit(unit);
        return unit;
    }

    private static TacticalUnit Civilian(TacticalState state, int x, int y)
    {
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Neutral, "civilian", "Civilian")
        {
            Position = new GridPos(x, y),
            Hp = 3,
            MaxHp = 3,
        };
        state.AddUnit(unit);
        return unit;
    }

    internal static IReadOnlyList<GameEvent> EnemyPhase(
        TacticalState state,
        AbilityRegistry? abilities = null,
        Action<AiTiming>? timing = null
    ) =>
        new TacticalEngine(
            state,
            Data,
            new UtilityAi(Data, timingLog: timing),
            abilities
        ).EndPlayerPhase();

    private static IEnumerable<GameEvent> Acts(IEnumerable<GameEvent> events, TacticalUnit unit) =>
        events.Where(e =>
            e is UnitMoved m && m.Unit == unit.Id || e is AbilityUsed a && a.Unit == unit.Id
        );

    // ------------------------------------------------------------------ rifleman

    [Fact]
    public void WardenTakesFullCoverOverOpenGround()
    {
        // Soldier at (8,0); the crate at (4,2) gives (4,3) full cover against it, with a clear view.
        var state = Field(1, "..........", "..........", "....C.....", "..........", "..........");
        var warden = Enemy(state, "warden", 4, 4);
        warden.Mobility = 1;
        Soldier(state, 8, 0);

        EnemyPhase(state);

        Assert.Equal(new GridPos(4, 3), warden.Position);
        Assert.Equal(
            CoverLevel.Full,
            CoverCalculator.Evaluate(state.Map, warden.Position, new GridPos(8, 0)).Level
        );
    }

    [Fact]
    public void WardenShootsAFlankedTargetInsteadOfRepositioning()
    {
        var state = Field(2, Open(14, 5));
        var warden = Enemy(state, "warden", 2, 2);
        warden.Aim = 70;
        var soldier = Soldier(state, 9, 2);

        var events = EnemyPhase(state);

        Assert.Equal(new GridPos(2, 2), warden.Position);
        var shot = Assert.IsType<AbilityUsed>(Acts(events, warden).First());
        Assert.Equal(AbilityRegistry.Shoot, shot.AbilityId);
        Assert.Equal(Target.Unit(soldier.Id), shot.Target);
        var resolved = events.OfType<ShotResolved>().First();
        Assert.Equal(70, resolved.HitChance);
    }

    [Fact]
    public void WardenOverwatchesWhenNoShotIsWorthTaking()
    {
        // The soldier stands behind full cover that the warden's aim cannot beat.
        var state = Field(3, "..........", "..........", "......C...", "..........", "..........");
        var warden = Enemy(state, "warden", 2, 2);
        warden.Aim = 30;
        warden.Mobility = 0;
        Soldier(state, 7, 2);

        var events = EnemyPhase(state);

        Assert.Contains(
            events,
            e => e is AbilityUsed { AbilityId: AbilityRegistry.Overwatch } a && a.Unit == warden.Id
        );
        Assert.DoesNotContain(events, e => e is AbilityUsed { AbilityId: AbilityRegistry.Shoot });
    }

    // ------------------------------------------------------------------ rusher

    [Fact]
    public void HuskAdjacentToASoldierAttacks()
    {
        var state = Field(4, Open(10, 3));
        var husk = Enemy(state, "husk", 3, 1);
        var soldier = Soldier(state, 4, 1);

        var events = EnemyPhase(state);

        var act = Assert.IsType<AbilityUsed>(Acts(events, husk).First());
        Assert.Equal(AbilityRegistry.Melee, act.AbilityId);
        Assert.Equal(Target.Unit(soldier.Id), act.Target);
        Assert.Equal(new GridPos(3, 1), husk.Position);
    }

    [Fact]
    public void HuskTenTilesAwayDashesTowardTheSoldier()
    {
        var state = Field(5, Open(24, 3));
        var husk = Enemy(state, "husk", 2, 1);
        husk.Mobility = 4;
        husk.Sight = 14;
        var soldier = Soldier(state, 12, 1);

        var events = EnemyPhase(state);

        var move = Assert.IsType<UnitMoved>(Acts(events, husk).First());
        Assert.True(move.Dashed);
        Assert.True(husk.Position.DistanceTo(soldier.Position) <= 2.5);
    }

    [Fact]
    public void HuskGoesForACivilianWithinSixTiles()
    {
        var state = Field(6, Open(24, 5));
        var husk = Enemy(state, "husk", 2, 2);
        husk.Sight = 14;
        Soldier(state, 12, 2);
        var civilian = Civilian(state, 6, 4);

        var events = EnemyPhase(state);

        var melee = events
            .OfType<AbilityUsed>()
            .First(a => a.Unit == husk.Id && a.AbilityId == AbilityRegistry.Melee);
        Assert.Equal(Target.Unit(civilian.Id), melee.Target);
    }

    // ------------------------------------------------------------------ flanker

    [Fact]
    public void ProxyPrefersAFlankingTileOverHalfCover()
    {
        // The soldier hides behind a crate to its west; the proxy already stands in half cover.
        var state = Field(
            7,
            "..............",
            "..............",
            "..............",
            ".....c...c....",
            "..............",
            "..............",
            ".............."
        );
        var proxy = Enemy(state, "proxy", 5, 3);
        proxy.Mobility = 7;
        var soldier = Soldier(state, 10, 3);

        EnemyPhase(state);

        Assert.NotEqual(new GridPos(5, 3), proxy.Position);
        Assert.True(CoverCalculator.Evaluate(state.Map, soldier.Position, proxy.Position).Flanked);
    }

    // ------------------------------------------------------------------ heavy

    // Stand-in for #30's Suppression: marks the target suppressed.
    private sealed class StubSuppression : IAbilityHandler
    {
        public ValidationResult Validate(AbilityContext ctx) =>
            ctx.Target is UnitTarget t && LosTracerCanSee(ctx.State, ctx.User, ctx.State.Unit(t.Id))
                ? ValidationResult.Ok
                : ValidationResult.Fail("No line of sight.");

        public void Execute(AbilityContext ctx) =>
            ctx.State.Unit(((UnitTarget)ctx.Target).Id).Statuses.Set(StatusFlags.Suppressed);

        private static bool LosTracerCanSee(TacticalState s, TacticalUnit a, TacticalUnit b) =>
            Firewall.Rules.Tactical.Vision.LosTracer.CanSee(s, a, b);
    }

    [Fact]
    public void EnforcerSuppressesTheSoldierWithTheBestShotOnAnAlly()
    {
        var state = Field(
            8,
            "............",
            ".....c......",
            "............",
            "............",
            "............"
        );
        var enforcer = Enemy(state, "enforcer", 1, 1);
        enforcer.Mobility = 0;
        var warden = Enemy(state, "warden", 1, 3);
        warden.Mobility = 0;
        warden.Aim = 80;
        var covered = Soldier(state, 6, 3); // behind the crate, harder for the warden to hit
        var exposed = Soldier(state, 7, 0);
        var registry = AbilityRegistry.CreateDefault();
        registry.Register("suppression", new StubSuppression());

        var events = EnemyPhase(state, registry);

        var suppress = events.OfType<AbilityUsed>().First(a => a.Unit == enforcer.Id);
        Assert.Equal("suppression", suppress.AbilityId);
        Assert.Equal(Target.Unit(exposed.Id), suppress.Target);
        Assert.True(exposed.Statuses.Suppressed);
        Assert.False(covered.Statuses.Suppressed);
    }

    // ------------------------------------------------------------------ fallbacks

    [Fact]
    public void WithNobodyInSightAndNothingLeftToSearchAWardenOverwatches()
    {
        // The soldier is sealed off behind a wall, and everything the warden can reach is in view.
        var state = Field(9, "...#...", "...#...", "...#...");
        var warden = Enemy(state, "warden", 1, 1);
        Soldier(state, 5, 1);

        var events = EnemyPhase(state);

        Assert.Contains(
            events,
            e => e is AbilityUsed { AbilityId: AbilityRegistry.Overwatch } a && a.Unit == warden.Id
        );
        Assert.Equal(new GridPos(1, 1), warden.Position);
    }

    [Fact]
    public void WithNothingToDoAHuskDoesNothing()
    {
        var state = Field(10, "...#...", "...#...", "...#...");
        var husk = Enemy(state, "husk", 1, 1);
        Soldier(state, 5, 1);

        var events = EnemyPhase(state);

        Assert.Empty(Acts(events, husk));
    }

    [Fact]
    public void WithNobodyKnownAnActiveUnitSearchesUnexploredGround()
    {
        // GDD §4.9: an active enemy that has lost the squad sweeps toward ground it has not seen,
        // so a mission cannot stall with one enemy overwatching a corner forever.
        foreach (var id in new[] { "warden", "husk" })
        {
            var state = Field(12, Open(40, 3));
            var enemy = Enemy(state, id, 1, 1);
            Soldier(state, 38, 1);

            var events = EnemyPhase(state);

            Assert.True(enemy.Position.X > 1, $"{id} stayed at {enemy.Position}");
            Assert.DoesNotContain(
                events,
                e =>
                    e is AbilityUsed { AbilityId: AbilityRegistry.Overwatch } a
                    && a.Unit == enemy.Id
            );
        }
    }

    [Fact]
    public void ASearchingEnemyEventuallyFindsAHiddenSquad()
    {
        var state = Field(13, Open(40, 3));
        var warden = Enemy(state, "warden", 1, 1);
        var soldier = Soldier(state, 38, 1);
        var engine = new TacticalEngine(state, Data, new UtilityAi(Data));

        for (var turn = 0; turn < 12 && state.Result is null && soldier.IsAlive; turn++)
        {
            engine.EndPlayerPhase();
            if (state.EnemyVision.IsUnitVisible(soldier))
                break;
        }

        Assert.True(state.EnemyVision.IsUnitVisible(soldier) || !soldier.IsAlive);
        Assert.True(warden.Position.X > 20);
    }

    [Fact]
    public void ALoneUnitWithoutOverwatchHunkersDownNextToCover()
    {
        var state = Field(11, "......#.", "..c...#.", "......#.");
        var enforcer = Enemy(state, "enforcer", 3, 1);
        enforcer.Abilities.Add(AbilityRegistry.HunkerDown);
        Soldier(state, 7, 0);

        var events = EnemyPhase(state);

        Assert.Contains(events, e => e is StatusAdded { Status: StatusFlags.Hunkered });
    }

    // ------------------------------------------------------------------ GDD 4.9 scoring

    private static IReadOnlyList<GameEvent> Phase(TacticalState state, AiDifficulty difficulty) =>
        new TacticalEngine(state, Data, new UtilityAi(Data, difficulty)).EndPlayerPhase();

    private static readonly AiDifficulty Steady = new(0, 1);

    [Fact]
    public void ADashIsOnlyUsedWhenNoOneApTileHasAShot()
    {
        // In range already: the warden shoots without dashing.
        var near = Field(20, Open(24, 3));
        var warden = Enemy(near, "warden", 2, 1);
        Soldier(near, 9, 1, hp: 100);
        var shot = Phase(near, Steady);
        Assert.DoesNotContain(shot, e => e is UnitMoved { Dashed: true });
        Assert.Contains(shot, e => e is AbilityUsed { AbilityId: AbilityRegistry.Shoot });
        Assert.NotNull(warden);

        // Out of reach of any shot from a 1 AP tile: it dashes to close in.
        var far = Field(21, Open(40, 3));
        far.Map.SetTile(new GridPos(0, 0), TileType.Floor);
        var distant = Enemy(far, "warden", 1, 1);
        distant.Sight = 30;
        Soldier(far, 36, 1, hp: 100);
        var dash = Phase(far, Steady);
        Assert.Contains(dash, e => e is UnitMoved { Dashed: true } m && m.Unit == distant.Id);
    }

    [Fact]
    public void ARiflemanClosesToItsPreferredRangeBandAndNoFurther()
    {
        var state = Field(22, Open(40, 3));
        var warden = Enemy(state, "warden", 1, 1);
        warden.Sight = 30;
        var soldier = Soldier(state, 30, 1, hp: 1000);
        var engine = new TacticalEngine(state, Data, new UtilityAi(Data, Steady));
        var profile = Data.AiProfiles.First(p => p.Kind == AiKind.CoverShooter);

        for (var turn = 0; turn < 4; turn++)
            engine.EndPlayerPhase();

        var distance = warden.Position.DistanceTo(soldier.Position);
        // It stops where one more step would cost more than the range penalty it removes.
        Assert.InRange(distance, profile.PreferredRangeMin, profile.PreferredRangeMax + 2);
    }

    [Fact]
    public void AUnitMovesAndShootsInTheSameTurnWhenItHasNoShotFromWhereItStands()
    {
        // A wall hides the soldier from the warden's tile, but a step north opens the view.
        var state = Field(23, "..........", "..........", "....#.....", "..........");
        var warden = Enemy(state, "warden", 3, 1);
        var soldier = Soldier(state, 6, 1, hp: 100);
        var left = Enemy(state, "husk", 9, 0);
        left.Hp = left.MaxHp = 100;

        var events = Phase(state, Steady).Where(e => Acts([e], warden).Any()).ToList();

        var move = events.FindIndex(e => e is UnitMoved);
        var shot = events.FindIndex(e => e is AbilityUsed { AbilityId: AbilityRegistry.Shoot });
        Assert.True(move >= 0 && shot > move, "expected a move followed by a shot");
        Assert.NotNull(soldier);
    }

    [Fact]
    public void TheKillBonusPicksTheTargetItCanFinishOff()
    {
        var state = Field(24, Open(14, 5));
        var warden = Enemy(state, "warden", 1, 2);
        warden.Aim = 100;
        warden.Mobility = 0;
        var healthy = Soldier(state, 8, 1, hp: 100);
        var dying = Soldier(state, 8, 3, hp: 1);

        var events = Phase(state, Steady);

        var shot = events.OfType<AbilityUsed>().First(a => a.AbilityId == AbilityRegistry.Shoot);
        Assert.Equal(Target.Unit(dying.Id), shot.Target);
        Assert.NotEqual(Target.Unit(healthy.Id), shot.Target);
    }

    [Fact]
    public void SeededNoiseLetsDifferentSeedsPickDifferentTiles()
    {
        // Many equivalent open tiles: noise alone decides, so seeds spread out; no noise does not.
        HashSet<GridPos> Spread(AiDifficulty difficulty)
        {
            var ends = new HashSet<GridPos>();
            for (ulong seed = 1; seed <= 16; seed++)
            {
                var state = Field(seed, Open(30, 9));
                var proxy = Enemy(state, "proxy", 4, 4);
                Soldier(state, 14, 4, hp: 100);
                Phase(state, difficulty);
                ends.Add(proxy.Position);
            }
            return ends;
        }

        Assert.Single(Spread(Steady));
        Assert.True(Spread(AiDifficulty.Recruit).Count > 1);
    }

    [Fact]
    public void RecruitBlursChoicesMoreThanVeteran()
    {
        Assert.True(AiDifficulty.Recruit.NoiseScale > AiDifficulty.Normal.NoiseScale);
        Assert.True(AiDifficulty.Normal.NoiseScale > AiDifficulty.Veteran.NoiseScale);
        Assert.True(AiDifficulty.Recruit.FlankScale < AiDifficulty.Normal.FlankScale);
        Assert.True(AiDifficulty.Normal.FlankScale < AiDifficulty.Veteran.FlankScale);

        int Spread(AiDifficulty difficulty)
        {
            var ends = new HashSet<GridPos>();
            for (ulong seed = 1; seed <= 24; seed++)
            {
                var state = Field(seed, Open(30, 9));
                var proxy = Enemy(state, "proxy", 4, 4);
                Soldier(state, 14, 4, hp: 100);
                Phase(state, difficulty);
                ends.Add(proxy.Position);
            }
            return ends.Count;
        }

        Assert.True(Spread(AiDifficulty.Recruit) >= Spread(AiDifficulty.Veteran));
    }

    [Fact]
    public void ProxiesPreferToStayNearRobotAllies()
    {
        TacticalState Build(bool allies)
        {
            var state = Field(25, Open(24, 9));
            var proxy = Enemy(state, "proxy", 4, 4);
            proxy.Mobility = 5;
            if (allies)
                foreach (var y in new[] { 1, 7 })
                {
                    var robot = Enemy(state, "warden", 11, y);
                    robot.Mobility = 0;
                    robot.Aim = 0;
                }
            Soldier(state, 18, 4, hp: 1000);
            return state;
        }

        var alone = Build(false);
        var proxyAlone = alone.Units.First(u => u.DefId == "proxy");
        Phase(alone, Steady);

        var escorted = Build(true);
        var proxyEscorted = escorted.Units.First(u => u.DefId == "proxy");
        Phase(escorted, Steady);

        var robots = escorted.Units.Where(u => u.DefId == "warden").ToList();
        Assert.All(robots, r => Assert.True(proxyEscorted.Position.DistanceTo(r.Position) <= 4.5));
        Assert.NotEqual(proxyAlone.Position, proxyEscorted.Position);
    }

    [Fact]
    public void UnitsAvoidWalkingThroughASoldiersOverwatchWhenCoverIsNotWorthIt()
    {
        TacticalState Build(bool overwatch)
        {
            var state = Field(
                26,
                "............",
                "............",
                "...C........",
                "............",
                "...c........",
                "............",
                "............"
            );
            var warden = Enemy(state, "warden", 2, 2);
            warden.Mobility = 4;
            var soldier = Soldier(state, 11, 2, hp: 100);
            soldier.Statuses.Set(StatusFlags.Overwatch, overwatch);
            return state;
        }

        var calm = Build(false);
        var calmWarden = calm.Units.First(u => u.DefId == "warden");
        Phase(calm, Steady);
        var watched = Build(true);
        var watchedWarden = watched.Units.First(u => u.DefId == "warden");
        var events = Phase(watched, Steady);

        // Full cover is worth the walk until a soldier watches the way.
        Assert.NotEqual(new GridPos(2, 2), calmWarden.Position);
        Assert.Equal(new GridPos(2, 2), watchedWarden.Position);
        Assert.DoesNotContain(events, e => e is ReactionShotTriggered);
    }

    [Fact]
    public void HusksIgnoreCoverAndHeadStraightForTheNearestSoldier()
    {
        var state = Field(
            27,
            "....................",
            "....................",
            "..C.................",
            "....................",
            "...................."
        );
        var husk = Enemy(state, "husk", 3, 2);
        husk.Mobility = 5;
        husk.Sight = 14;
        var soldier = Soldier(state, 16, 2, hp: 100);

        Phase(state, Steady);

        Assert.True(husk.Position.DistanceTo(soldier.Position) <= 5);
    }

    // ------------------------------------------------------------------ determinism & budget

    [Fact]
    public void AUnitWithNothingToDoStaysPutInsteadOfWandering()
    {
        // The enforcer knows about the soldier (the warden sees it) but is walled in without a shot.
        var walls = new[]
        {
            "#####.......",
            "#...#.......",
            "#...#.......",
            "#...#.......",
            "#####.......",
        };
        foreach (var seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            var state = Field(seed, walls);
            var enforcer = Enemy(state, "enforcer", 2, 2);
            enforcer.Mobility = 2;
            var warden = Enemy(state, "warden", 7, 2);
            warden.Mobility = 0;
            Soldier(state, 10, 2);

            var events = EnemyPhase(state);

            Assert.Empty(Acts(events, enforcer));
            Assert.Equal(new GridPos(2, 2), enforcer.Position);
        }
    }

    [Fact]
    public void TheSameSeedAndStateGiveTheSameTurn()
    {
        TacticalState Build()
        {
            var state = Field(
                99,
                "................",
                "..c.......c.....",
                "................",
                ".......c........",
                "................"
            );
            Enemy(state, "warden", 1, 1);
            Enemy(state, "proxy", 2, 3);
            Enemy(state, "husk", 1, 2);
            Enemy(state, "enforcer", 3, 0);
            Soldier(state, 12, 1);
            Soldier(state, 11, 3);
            return state;
        }

        var (a, b) = (Build(), Build());
        var first = EnemyPhase(a);
        var second = EnemyPhase(b);

        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(first.Select(e => e.GetType()), second.Select(e => e.GetType()));
    }

    [Fact]
    public void ProfilesAreDefinedForEveryBehaviourKind()
    {
        foreach (var kind in Enum.GetValues<AiKind>().Where(k => k != AiKind.Stationary))
            Assert.Single(Data.AiProfiles, p => p.Kind == kind);
    }
}

/// <summary>Wall-clock budget, run alone so other test classes do not compete for the CPU.</summary>
[Collection(nameof(TimingTests))]
public class UtilityAiTimingTests
{
    [Fact]
    public void ABigMapStaysWithinBudgetAndTheTurnEnds()
    {
        var state = UtilityAiTests.Field(12, UtilityAiTests.Open(48, 48));
        var timings = new List<AiTiming>();
        foreach (var id in new[] { "warden", "warden", "proxy", "enforcer", "husk" })
            UtilityAiTests
                .Enemy(state, id, 20 + timings.Count % 3, 20 + state.Units.Count)
                .Mobility = 8;
        UtilityAiTests.Soldier(state, 4, 8);
        UtilityAiTests.Soldier(state, 6, 12);
        UtilityAiTests.Soldier(state, 3, 30).Sight = 6;

        UtilityAiTests.EnemyPhase(state, timing: timings.Add);

        Assert.Equal(5, timings.Count);
        // Native, not WASM; the point is to catch a planner that explodes.
        Assert.All(
            timings,
            t => Assert.True(t.Milliseconds < 1000, $"{t.Profile}: {t.Milliseconds} ms")
        );
    }
}
