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

    private static TacticalState Field(ulong seed, params string[] rows) =>
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

    private static string[] Open(int width, int height) =>
        Enumerable.Range(0, height).Select(_ => new string('.', width)).ToArray();

    private static TacticalUnit Soldier(TacticalState state, int x, int y, int hp = 10)
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

    private static TacticalUnit Enemy(TacticalState state, string defId, int x, int y)
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

    private static IReadOnlyList<GameEvent> EnemyPhase(
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
    public void WithNobodyInSightAWardenOverwatches()
    {
        var state = Field(9, Open(30, 3));
        var warden = Enemy(state, "warden", 1, 1);
        Soldier(state, 28, 1);

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
        var state = Field(10, Open(30, 3));
        var husk = Enemy(state, "husk", 1, 1);
        Soldier(state, 28, 1);

        var events = EnemyPhase(state);

        Assert.Empty(Acts(events, husk));
    }

    [Fact]
    public void ALoneUnitWithoutOverwatchHunkersDownNextToCover()
    {
        var state = Field(11, "......", "..c...", "......");
        var enforcer = Enemy(state, "enforcer", 3, 1);
        enforcer.Abilities.Add(AbilityRegistry.HunkerDown);
        enforcer.Sight = 1;
        Soldier(state, 5, 0);

        var events = EnemyPhase(state);

        Assert.Contains(events, e => e is StatusAdded { Status: StatusFlags.Hunkered });
    }

    // ------------------------------------------------------------------ determinism & budget

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
    public void ABigMapStaysWithinBudgetAndTheTurnEnds()
    {
        var state = Field(12, Open(48, 48));
        var timings = new List<AiTiming>();
        foreach (var id in new[] { "warden", "warden", "proxy", "enforcer", "husk" })
            Enemy(state, id, 20 + timings.Count % 3, 20 + state.Units.Count).Mobility = 8;
        Soldier(state, 4, 8);
        Soldier(state, 6, 12);
        Soldier(state, 3, 30).Sight = 6;

        EnemyPhase(state, timing: timings.Add);

        Assert.Equal(5, timings.Count);
        // Native, not WASM; the point is to catch a planner that explodes.
        Assert.All(
            timings,
            t => Assert.True(t.Milliseconds < 1000, $"{t.Profile}: {t.Milliseconds} ms")
        );
    }

    [Fact]
    public void ProfilesAreDefinedForEveryBehaviourKind()
    {
        foreach (var kind in Enum.GetValues<AiKind>().Where(k => k != AiKind.Stationary))
            Assert.Single(Data.AiProfiles, p => p.Kind == kind);
    }
}
