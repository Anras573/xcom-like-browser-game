using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;

namespace Firewall.Rules.Tests;

/// <summary>Pod patrol, activation and scamper (#25).</summary>
public class PodTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    // The same data with chain activation switched on for the Purge mission.
    private static readonly GameData ChainData = GameData.Load(f =>
        f == GameDataFiles.Missions
            ? File.ReadAllText(Path.Combine(DataDir, f))
                .Replace("\"podChainActivation\": 0", "\"podChainActivation\": 1")
            : File.ReadAllText(Path.Combine(DataDir, f))
    );

    // Rows top first. A 30 x 3 field of floor, with optional half crates ('c') on the given row.
    private static TacticalState Field(ulong seed = 7, params (int X, int Row)[] crates)
    {
        var rows = Enumerable.Range(0, 3).Select(_ => new char[30]).ToArray();
        foreach (var row in rows)
            Array.Fill(row, '.');
        foreach (var (x, row) in crates)
            rows[row][x] = 'c';
        var map = MapParser.Parse(
            new MapFile
            {
                Id = "t",
                Name = "T",
                Biome = Biome.Industrial,
                Rows = rows.Select(r => new string(r)).ToList(),
            },
            Data.Tiles
        );
        return new TacticalState(map, new Pcg32Random(seed));
    }

    private static TacticalUnit Rookie(TacticalState state, int x, int y = 1, int sight = 12)
    {
        var weapon = Data.Weapons["rifle_t1"];
        var unit = new TacticalUnit(state.NextUnitId(), Faction.Firewall, "soldier", "Rookie")
        {
            Position = new GridPos(x, y),
            Hp = 4,
            MaxHp = 4,
            Aim = 65,
            Mobility = 20,
            Sight = sight,
            ActionPoints = 2,
            Weapon = new WeaponState(weapon, weapon.Clip),
        };
        unit.Abilities.UnionWith(AbilityRegistry.StandardActions);
        state.AddUnit(unit);
        return unit;
    }

    /// <summary>A dormant pod of the given enemies, the first on (x, 1) and the rest next to it.</summary>
    private static Pod SpawnPod(
        TacticalState state,
        string name,
        int x,
        int mobility,
        int sight,
        params string[] defIds
    )
    {
        var id = state.AddPod(name, new GridPos(x, 1));
        var pod = state.Pods[^1];
        for (var i = 0; i < defIds.Length; i++)
        {
            var def = Data.Enemies[defIds[i]];
            var unit = new TacticalUnit(state.NextUnitId(), Faction.Meridian, def.Id, def.Name)
            {
                Position = new GridPos(x + i, 1),
                Hp = 100,
                MaxHp = 100,
                Aim = def.Aim,
                Mobility = mobility,
                Defense = def.Defense,
                Sight = sight,
                IsActive = false,
                Pod = id,
            };
            unit.Abilities.UnionWith(def.Abilities);
            state.AddUnit(unit);
            pod.Members.Add(unit.Id);
        }
        return pod;
    }

    private static TacticalUnit Member(TacticalState state, Pod pod, int index) =>
        state.Unit(pod.Members[index]);

    /// <summary>Counts how often each unit is handed a normal turn.</summary>
    private sealed class CountingAi : IAiController
    {
        public Dictionary<UnitId, int> Turns { get; } = [];

        public void TakeTurn(TacticalUnit unit, AiContext ctx) =>
            Turns[unit.Id] = Turns.GetValueOrDefault(unit.Id) + 1;
    }

    private static MoveCommand MoveTo(TacticalUnit unit, int x) => new(unit.Id, new GridPos(x, 1));

    // ------------------------------------------------------------------ activation

    [Fact]
    public void PodStaysDormantUntilAMemberSeesASoldier()
    {
        var state = Field();
        var soldier = Rookie(state, 0);
        var pod = SpawnPod(state, "Warden Patrol", 20, 6, 5, "warden", "warden");
        var e = new TacticalEngine(state, Data);

        var far = e.Execute(MoveTo(soldier, 10));
        Assert.False(pod.Active);
        Assert.DoesNotContain(far, ev => ev is PodActivated or UnitScampered);
        Assert.All(pod.Members, id => Assert.False(state.Unit(id).IsActive));

        var near = e.Execute(MoveTo(soldier, 16));
        Assert.True(pod.Active);
        var activated = Assert.Single(near.OfType<PodActivated>());
        Assert.Equal(pod.Id, activated.Pod);
        Assert.Equal("Warden Patrol", activated.Name);
        Assert.Equal(pod.Members, activated.Members);
        Assert.Equal(2, near.OfType<UnitScampered>().Select(s => s.Unit).Distinct().Count());
        Assert.All(pod.Members, id => Assert.True(state.Unit(id).IsActive));
        // The reveal comes after the step that caused it.
        Assert.True(
            near.ToList().FindIndex(ev => ev is PodActivated)
                > near.ToList().FindIndex(ev => ev is UnitMoved)
        );
    }

    [Fact]
    public void ShootingAMemberFromSquadsightRangeActivatesThePod()
    {
        var state = Field();
        var soldier = Rookie(state, 0);
        // 10 tiles away: the soldier sees the warden, the warden (sight 5) does not see back.
        var pod = SpawnPod(state, "Warden Patrol", 10, 6, 5, "warden", "warden");
        var e = new TacticalEngine(state, Data);
        Assert.False(pod.Active);

        var events = e.Execute(
            new UseAbilityCommand(
                soldier.Id,
                AbilityRegistry.Shoot,
                Target.Unit(Member(state, pod, 0).Id)
            )
        );

        Assert.True(pod.Active);
        var all = events.ToList();
        Assert.True(
            all.FindIndex(ev => ev is ShotResolved) < all.FindIndex(ev => ev is PodActivated)
        );
    }

    [Fact]
    public void ScamperCostsNoApAndEndsOnTheBestCoverTile()
    {
        // A half crate at (17, row 0 = y 2): the tile east of it, (18, 2), is covered from the west.
        var state = Field(crates: [(17, 0)]);
        var soldier = Rookie(state, 5);
        var pod = SpawnPod(state, "Warden Patrol", 15, 6, 12, "warden");
        var warden = Member(state, pod, 0);
        warden.ActionPoints = 2;
        var e = new TacticalEngine(state, Data);

        var events = e.Execute(MoveTo(soldier, 6));

        Assert.True(pod.Active);
        Assert.Equal(new GridPos(18, 2), warden.Position);
        Assert.Equal(
            CoverLevel.Half,
            CoverCalculator.Evaluate(state.Map, warden.Position, soldier.Position).Level
        );
        Assert.Equal(2, warden.ActionPoints);
        Assert.DoesNotContain(events, ev => ev is ApChanged a && a.Unit == warden.Id);
        var scamper = Assert.Single(events.OfType<UnitScampered>());
        Assert.Equal(warden.Position, scamper.Path[^1]);
    }

    [Fact]
    public void HusksChargeTheNearestSoldierAndDoNotAttackOnScamper()
    {
        var state = Field();
        var soldier = Rookie(state, 10);
        var pod = SpawnPod(state, "Husk Swarm", 20, 4, 12, "husk", "husk");
        var e = new TacticalEngine(state, Data);
        var before = Member(state, pod, 0).Position;

        var events = e.Execute(MoveTo(soldier, 9));

        Assert.True(pod.Active);
        Assert.True(
            Member(state, pod, 0).Position.DistanceTo(soldier.Position)
                < before.DistanceTo(soldier.Position)
        );
        Assert.DoesNotContain(events, ev => ev is ShotResolved or UnitDamaged);
        Assert.Equal(4, soldier.Hp);
    }

    [Fact]
    public void ChainActivationIsOffUnlessTheMissionTurnsItOn()
    {
        foreach (var (data, expected) in new[] { (Data, false), (ChainData, true) })
        {
            var state = Field();
            var soldier = Rookie(state, 0);
            var first = SpawnPod(state, "A", 10, 6, 12, "warden");
            // 3 tiles from the first pod, but too far to see the soldier itself.
            var second = SpawnPod(state, "B", 13, 6, 5, "husk");
            var e = new TacticalEngine(state, data);

            e.Execute(MoveTo(soldier, 1));

            Assert.True(first.Active);
            Assert.Equal(expected, second.Active);
        }
    }

    // ------------------------------------------------------------------ enemy phase

    [Fact]
    public void PodActivatedByItsOwnPatrolScampersAndDoesNotAlsoTakeItsTurn()
    {
        var state = Field();
        Rookie(state, 0);
        var pod = SpawnPod(state, "Warden Patrol", 8, 6, 5, "warden", "warden");
        // The patrol heads for the soldier's side of the field, so a step brings it into sight.
        pod.PatrolTarget = new GridPos(3, 1);
        var ai = new CountingAi();
        var e = new TacticalEngine(state, Data, ai);

        var events = e.EndPlayerPhase();

        Assert.True(pod.Active);
        var moveIndex = events
            .ToList()
            .FindIndex(ev => ev is UnitMoved m && m.Unit == pod.Members[0]);
        var activated = events.ToList().FindIndex(ev => ev is PodActivated);
        Assert.InRange(moveIndex, 0, activated - 1);
        Assert.NotEmpty(events.OfType<UnitScampered>());
        Assert.Empty(ai.Turns);
        Assert.All(pod.Members, id => Assert.Equal(0, state.Unit(id).ActionPoints));

        // Next enemy phase it acts like any other pod.
        e.Execute(new EndTurnCommand());
        Assert.All(pod.Members, id => Assert.Equal(1, ai.Turns[id]));
    }

    [Fact]
    public void DormantPodsPatrolAsAGroupWithinRangeOfTheirSpawn()
    {
        var state = Field();
        Rookie(state, 0, sight: 3);
        var pod = SpawnPod(state, "Warden Patrol", 22, 6, 5, "warden", "warden", "warden");
        var e = new TacticalEngine(state, Data);
        var start = Member(state, pod, 0).Position;

        for (var turn = 0; turn < 6; turn++)
        {
            e.EndPlayerPhase();
            Assert.False(pod.Active);
            Assert.True(
                pod.PatrolTarget.DistanceTo(pod.Spawn) <= TacticalEngine.PatrolRadius + 1,
                $"target {pod.PatrolTarget} is too far from {pod.Spawn}"
            );
        }

        var leader = Member(state, pod, 0);
        Assert.NotEqual(start, leader.Position);
        foreach (var follower in pod.Members.Skip(1).Select(state.Unit))
            Assert.True(follower.Position.DistanceTo(leader.Position) <= 4);
    }

    [Fact]
    public void PatrolIsDeterministic()
    {
        ulong Run()
        {
            var state = Field(seed: 11);
            Rookie(state, 0, sight: 3);
            SpawnPod(state, "Warden Patrol", 22, 6, 5, "warden", "warden");
            var e = new TacticalEngine(state, Data);
            for (var i = 0; i < 4; i++)
                e.EndPlayerPhase();
            return state.StateHash();
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void PatrolMovesTriggerOverwatch()
    {
        var state = Field();
        var soldier = Rookie(state, 0);
        var pod = SpawnPod(state, "Warden Patrol", 15, 6, 5, "warden");
        pod.PatrolTarget = new GridPos(3, 1);
        var warden = Member(state, pod, 0);
        var e = new TacticalEngine(state, Data);
        e.Execute(new UseAbilityCommand(soldier.Id, AbilityRegistry.Overwatch, Target.None));

        var events = e.EndPlayerPhase();

        var trigger = Assert.Single(events.OfType<ReactionShotTriggered>());
        Assert.Equal((soldier.Id, warden.Id), (trigger.Shooter, trigger.Mover));
        // The warden walked into the soldier's sight without seeing it (sight 5 against 12), but the
        // shot at it wakes the pod, and the walk ends there.
        var all = events.ToList();
        Assert.Contains(events, ev => ev is ShotResolved s && s.Attacker == soldier.Id);
        Assert.True(
            all.FindIndex(ev => ev is ShotResolved) < all.FindIndex(ev => ev is PodActivated)
        );
        Assert.True(pod.Active);
        Assert.True(warden.Position.DistanceTo(soldier.Position) > 5);
    }
}
