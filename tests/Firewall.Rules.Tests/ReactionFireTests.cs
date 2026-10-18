using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;

namespace Firewall.Rules.Tests;

/// <summary>Overwatch, reaction fire, Hunker Down and Reload (#23).</summary>
public class ReactionFireTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    // Three rows of floor, 20 wide; an optional crate at (cx, 1).
    private static TacticalState Yard(ulong seed = 7, int? crateX = null)
    {
        var middle = new string('.', 20).ToCharArray();
        if (crateX is { } cx)
            middle[cx] = 'c';
        var map = MapParser.Parse(
            new MapFile
            {
                Id = "t",
                Name = "T",
                Biome = Biome.Industrial,
                Rows = [new string('.', 20), new string(middle), new string('.', 20)],
            },
            Data.Tiles
        );
        return new TacticalState(map, new Pcg32Random(seed));
    }

    private static TacticalUnit Rookie(TacticalState state, int x, int y = 1)
    {
        var weapon = Data.Weapons["rifle_t1"];
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Firewall, "soldier", "Rookie")
        {
            Position = new GridPos(x, y),
            Hp = 4,
            MaxHp = 4,
            Aim = 65,
            Mobility = 20,
            Sight = 5,
            ActionPoints = 2,
            Weapon = new WeaponState(weapon, weapon.Clip),
        };
        unit.Abilities.UnionWith(AbilityRegistry.StandardActions);
        state.AddUnit(unit);
        return unit;
    }

    private static TacticalUnit Enemy(TacticalState state, string defId, int x, int y = 1)
    {
        var def = Data.Enemies[defId];
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Meridian, defId, def.Name)
        {
            Position = new GridPos(x, y),
            Hp = def.Hp,
            MaxHp = def.Hp,
            Aim = def.Aim,
            Mobility = 20,
            Defense = def.Defense,
            Armor = def.Armor,
            Sight = 5,
            ActionPoints = 2,
        };
        unit.Abilities.UnionWith(def.Abilities);
        state.AddUnit(unit);
        return unit;
    }

    /// <summary>Each enemy runs its scripted commands in its turn.</summary>
    private sealed class ScriptAi(Func<TacticalUnit, IEnumerable<ICommand>> script) : IAiController
    {
        public void TakeTurn(TacticalUnit unit, AiContext ctx)
        {
            foreach (var cmd in script(unit))
                ctx.Execute(cmd);
        }
    }

    private static UseAbilityCommand Use(TacticalUnit unit, string id) =>
        new(unit.Id, id, Target.None);

    private static IReadOnlyList<GameEvent> MoveInEnemyPhase(
        TacticalEngine engine,
        TacticalUnit mover
    )
    {
        var events = engine.EndPlayerPhase();
        Assert.Contains(events, e => e is UnitMoved m && m.Unit == mover.Id);
        return events;
    }

    private static TacticalEngine MoverEngine(
        TacticalState state,
        TacticalUnit mover,
        GridPos dest
    ) => new(state, Data, new ScriptAi(u => u.Id == mover.Id ? [new MoveCommand(u.Id, dest)] : []));

    // ------------------------------------------------------------------ overwatch

    [Fact]
    public void OverwatchTriggersOnTheFirstStepIntoSightOnceAndSpendsAmmo()
    {
        var state = Yard();
        var rookie = Rookie(state, 0);
        var husk = Enemy(state, "husk", 15);
        husk.Hp = husk.MaxHp = 100;
        var e = MoverEngine(state, husk, new GridPos(1, 1));

        var used = e.Execute(Use(rookie, AbilityRegistry.Overwatch));
        Assert.Equal(0, rookie.ActionPoints);
        Assert.True(rookie.Statuses.Overwatch);
        Assert.Contains(
            used,
            ev => ev is StatusAdded(var id, StatusFlags.Overwatch) && id == rookie.Id
        );
        var clip = rookie.Weapon!.Ammo;

        var events = MoveInEnemyPhase(e, husk);

        var trigger = Assert.Single(events.OfType<ReactionShotTriggered>());
        Assert.Equal((rookie.Id, husk.Id), (trigger.Shooter, trigger.Mover));
        var index = events.ToList().IndexOf(trigger);
        var before = (UnitMoved)events.Take(index).Last(ev => ev is UnitMoved);
        // The step that set it off is the first within sight (5 tiles); the one before was not.
        Assert.True(before.Path[^1].DistanceTo(rookie.Position) <= rookie.Sight);
        Assert.True(before.Path[^2].DistanceTo(rookie.Position) > rookie.Sight);
        Assert.IsType<UnitFacing>(events[index + 1]);
        Assert.IsType<ShotResolved>(events[index + 2]);
        Assert.Contains(
            events,
            ev => ev is StatusRemoved(var id, StatusFlags.Overwatch) && id == rookie.Id
        );

        Assert.Equal(clip - 1, rookie.Weapon.Ammo);
        Assert.False(rookie.Statuses.Overwatch);
        Assert.Empty(rookie.Reactions);
        Assert.Equal(new GridPos(1, 1), husk.Position);
    }

    [Fact]
    public void TwoOverwatchersFireInIdOrder()
    {
        var state = Yard();
        var first = Rookie(state, 0, 0);
        var second = Rookie(state, 0, 2);
        var husk = Enemy(state, "husk", 15);
        husk.Hp = husk.MaxHp = 100;
        var e = MoverEngine(state, husk, new GridPos(2, 1));
        e.Execute(Use(second, AbilityRegistry.Overwatch));
        e.Execute(Use(first, AbilityRegistry.Overwatch));

        var events = MoveInEnemyPhase(e, husk);

        Assert.Equal(
            [first.Id, second.Id],
            events.OfType<ReactionShotTriggered>().Select(t => t.Shooter)
        );
        Assert.False(first.Statuses.Overwatch);
        Assert.False(second.Statuses.Overwatch);
    }

    [Fact]
    public void WhenTheFirstOverwatcherKillsTheMoverTheSecondKeepsOverwatch()
    {
        var state = Yard();
        var first = Rookie(state, 0, 0);
        var second = Rookie(state, 0, 2);
        first.Aim = 200;
        var husk = Enemy(state, "husk", 15);
        husk.Hp = husk.MaxHp = 1;
        husk.Armor = 0;
        Enemy(state, "husk", 19, 2).Hp = 1; // keeps the mission running
        var e = MoverEngine(state, husk, new GridPos(2, 1));
        e.Execute(Use(first, AbilityRegistry.Overwatch));
        e.Execute(Use(second, AbilityRegistry.Overwatch));
        var ammo = second.Weapon!.Ammo;

        var events = e.EndPlayerPhase();

        var trigger = Assert.Single(events.OfType<ReactionShotTriggered>());
        Assert.Equal(first.Id, trigger.Shooter);
        Assert.Contains(events, ev => ev is UnitKilled(var id) && id == husk.Id);
        Assert.False(husk.IsAlive);
        Assert.NotEqual(new GridPos(2, 1), husk.Position); // the move stopped
        // Overwatch lasts until the second soldier's next turn, which has just begun.
        Assert.Contains(
            events,
            ev => ev is StatusRemoved(var id, StatusFlags.Overwatch) && id == second.Id
        );
        Assert.DoesNotContain(
            events.TakeWhile(ev => ev is not PhaseStarted { Phase: TacticalPhase.Environment }),
            ev => ev is StatusRemoved(var id, StatusFlags.Overwatch) && id == second.Id
        );
        Assert.Equal(ammo, second.Weapon.Ammo);
    }

    [Fact]
    public void AReactionShotCannotCritWithoutSentinel()
    {
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var (shot, _) = FlankedReactionShot(seed, sentinel: false);
            Assert.False(shot.Crit);
        }
    }

    [Fact]
    public void WithSentinelAReactionShotCanCrit()
    {
        var crits = 0;
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var (shot, preview) = FlankedReactionShot(seed, sentinel: true);
            Assert.Equal(60, preview.CritChance); // rifle 10 + flanked 50
            if (shot.Crit)
                crits++;
        }
        Assert.InRange(crits, 1, 29);
    }

    // A rookie that always hits overwatches a husk in the open (flanked) walking into sight.
    private static (ShotResolved Shot, ShotPreview Preview) FlankedReactionShot(
        ulong seed,
        bool sentinel
    )
    {
        var state = Yard(seed);
        var rookie = Rookie(state, 0);
        rookie.Aim = 200;
        if (sentinel)
            rookie.Abilities.Add(AbilityRegistry.Sentinel);
        var husk = Enemy(state, "husk", 15);
        husk.Hp = husk.MaxHp = 100;
        var e = MoverEngine(state, husk, new GridPos(1, 1));
        e.Execute(Use(rookie, AbilityRegistry.Overwatch));
        var reaction = ShotContext.For(Data, rookie) with
        {
            Reaction = true,
            ReactionCrit = sentinel,
        };
        var shot = Assert.Single(e.EndPlayerPhase().OfType<ShotResolved>());
        // Open ground: flanked wherever the husk was shot.
        var preview = HitCalculator.Preview(state, rookie, husk, reaction);
        Assert.True(preview.Flanked);
        return (shot, preview);
    }

    [Fact]
    public void OverwatchNeedsAmmo()
    {
        var state = Yard();
        var rookie = Rookie(state, 0);
        Enemy(state, "husk", 15);
        var e = new TacticalEngine(state, Data);
        rookie.Weapon!.Ammo = 0;

        Assert.Equal("Out of ammo.", e.Validate(Use(rookie, AbilityRegistry.Overwatch)).Reason);
    }

    [Fact]
    public void AnAlreadyVisibleUnitThatDoesNotMoveDoesNotTriggerOverwatch()
    {
        var state = Yard();
        var rookie = Rookie(state, 0);
        var warden = Enemy(state, "warden", 3);
        var e = new TacticalEngine(
            state,
            Data,
            new ScriptAi(u =>
                [new UseAbilityCommand(u.Id, AbilityRegistry.HunkerDown, Target.None)]
            )
        );
        warden.Abilities.Add(AbilityRegistry.HunkerDown);
        state.Map.SetTile(new GridPos(4, 1), TileType.SmallCrate);
        e.Execute(Use(rookie, AbilityRegistry.Overwatch));

        var events = e.EndPlayerPhase();

        Assert.Contains(events, ev => ev is AbilityUsed { AbilityId: AbilityRegistry.HunkerDown });
        Assert.DoesNotContain(events, ev => ev is ReactionShotTriggered);
    }

    [Fact]
    public void EnemyOverwatchShootsASoldierMovingInThePlayerPhase()
    {
        var state = Yard();
        var rookie = Rookie(state, 15);
        rookie.Hp = rookie.MaxHp = 100;
        var warden = Enemy(state, "warden", 0);
        var e = new TacticalEngine(
            state,
            Data,
            new ScriptAi(u => [new UseAbilityCommand(u.Id, AbilityRegistry.Overwatch, Target.None)])
        );
        var events = e.EndPlayerPhase();
        Assert.True(warden.Statuses.Overwatch);
        Assert.Contains(
            events,
            ev => ev is StatusAdded(var id, StatusFlags.Overwatch) && id == warden.Id
        );

        events = e.Execute(new MoveCommand(rookie.Id, new GridPos(1, 1)));

        var trigger = Assert.Single(events.OfType<ReactionShotTriggered>());
        Assert.Equal((warden.Id, rookie.Id), (trigger.Shooter, trigger.Mover));
        Assert.Contains(events, ev => ev is ShotResolved s && s.Attacker == warden.Id);
        Assert.False(warden.Statuses.Overwatch);
    }

    [Fact]
    public void ReactionFireReplaysToTheSameStateHash()
    {
        static TacticalState Create(ulong seed)
        {
            var state = Yard(seed);
            Rookie(state, 0, 0);
            Rookie(state, 0, 2);
            Enemy(state, "warden", 15).Hp = 100;
            return state;
        }
        IAiController Ai() => new ScriptAi(u => [new MoveCommand(u.Id, new GridPos(2, 1))]);

        var e = new TacticalEngine(Create(3), Data, Ai());
        var soldiers = e.State.Units.Where(u => u.Faction == Faction.Firewall).ToList();
        e.Execute(Use(soldiers[0], AbilityRegistry.Overwatch));
        e.Execute(Use(soldiers[1], AbilityRegistry.Overwatch));
        var events = e.EndPlayerPhase();
        Assert.Equal(2, events.OfType<ReactionShotTriggered>().Count());

        var replayed = TacticalEngine.Replay(e.Log.Seed, e.Log.Commands, Create, Data, Ai());
        Assert.Equal(e.State.StateHash(), replayed.State.StateHash());
    }

    // ------------------------------------------------------------------ hunker down

    [Fact]
    public void HunkerWithoutAdjacentCoverFailsWithAReason()
    {
        var state = Yard();
        var rookie = Rookie(state, 0);
        Enemy(state, "husk", 15);
        var e = new TacticalEngine(state, Data);

        var result = e.Validate(Use(rookie, AbilityRegistry.HunkerDown));
        Assert.False(result.IsValid);
        Assert.Contains("cover", result.Reason);
    }

    [Fact]
    public void HunkerDoublesCover_HalfCoverWardenGoesFrom35To15()
    {
        var state = Yard(crateX: 7);
        var rookie = Rookie(state, 0);
        rookie.Sight = 20;
        var warden = Enemy(state, "warden", 8);
        warden.Abilities.Add(AbilityRegistry.HunkerDown);
        var e = new TacticalEngine(state, Data);
        Assert.Equal(35, e.PreviewShot(rookie, warden)!.HitChance);

        state.Phase = TacticalPhase.Enemy;
        var events = e.Execute(Use(warden, AbilityRegistry.HunkerDown));

        Assert.True(warden.Statuses.Hunkered);
        Assert.Equal(0, warden.ActionPoints);
        Assert.Contains(
            events,
            ev => ev is StatusAdded(var id, StatusFlags.Hunkered) && id == warden.Id
        );
        Assert.Equal(15, e.PreviewShot(rookie, warden)!.HitChance);
    }

    // ------------------------------------------------------------------ reload

    [Fact]
    public void ReloadRestoresAmmoAndDoesNotEndTheTurn()
    {
        var state = Yard();
        var rookie = Rookie(state, 0);
        Enemy(state, "husk", 15);
        var e = new TacticalEngine(state, Data);
        rookie.Weapon!.Ammo = 0;

        var events = e.Execute(Use(rookie, AbilityRegistry.Reload));

        Assert.Equal(rookie.Weapon.Def.Clip, rookie.Weapon.Ammo);
        Assert.Equal(1, rookie.ActionPoints);
        Assert.Contains(events, ev => ev is AbilityUsed { AbilityId: AbilityRegistry.Reload });
        Assert.Contains(
            events,
            ev =>
                ev is AmmoChanged(var id, var ammo)
                && id == rookie.Id
                && ammo == rookie.Weapon.Def.Clip
        );
    }

    [Fact]
    public void ReloadingAFullClipFailsValidation()
    {
        var state = Yard();
        var rookie = Rookie(state, 0);
        Enemy(state, "husk", 15);
        var e = new TacticalEngine(state, Data);

        Assert.Equal(
            "The clip is already full.",
            e.Validate(Use(rookie, AbilityRegistry.Reload)).Reason
        );
    }
}
