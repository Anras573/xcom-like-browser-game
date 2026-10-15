using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;

namespace Firewall.Rules.Tests;

public class UnitDeploymentTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    private static TacticalMap Yard() =>
        Data.LoadMap(File.ReadAllText(Path.Combine(DataDir, "maps", "test-yard.json")));

    private static TacticalMap Map(params string[] rows) =>
        MapParser.Parse(
            new MapFile
            {
                Id = "t",
                Name = "T",
                Biome = Biome.Industrial,
                Rows = rows,
            },
            Data.Tiles
        );

    private static TacticalState NewState(TacticalMap map, ulong seed = 1) =>
        new(map, new Pcg32Random(seed));

    private static PodSpec Pod(string name, params string[] ids) => new(name, ids);

    private static IReadOnlyList<PodSpec> YardPods { get; } =
    [
        Pod("Husk Pack", "husk", "husk", "husk"),
        Pod("Warden Patrol", "warden", "warden"),
        Pod("Mixed", "warden", "husk", "husk", "husk"),
    ];

    private static TacticalState Skirmish(ulong seed)
    {
        var state = NewState(Yard(), seed);
        SquadDeployer.Deploy(state, DebugSquad.Create(state, Data, state.Rng.Fork("squad")));
        PodSpawner.Spawn(state, Data, YardPods, state.Rng.Fork("pods"));
        return state;
    }

    [Fact]
    public void DebugSquadIsFourRiflemenWithNames()
    {
        var state = NewState(Yard());
        var squad = DebugSquad.Create(state, Data, state.Rng);
        Assert.Equal(4, squad.Count);
        Assert.All(
            squad,
            u =>
            {
                Assert.Equal(Faction.Firewall, u.Faction);
                Assert.Equal(Data.Soldier.Hp, u.MaxHp);
                Assert.Equal(WeaponClass.Rifle, u.Weapon!.Def.Class);
                Assert.Equal(u.Weapon.Def.Clip, u.Weapon.Ammo);
                Assert.Contains(u.AppearanceId, Data.Soldier.AppearanceSets);
                Assert.Contains(' ', u.DisplayName);
            }
        );
        Assert.Equal(4, squad.Select(u => u.Id).Distinct().Count());
    }

    [Fact]
    public void DeployPutsFourSoldiersOnPlayerSpawnsInOrder()
    {
        var state = NewState(Yard());
        var squad = DebugSquad.Create(state, Data, state.Rng);
        SquadDeployer.Deploy(state, squad);

        Assert.Equal(state.Map.PlayerSpawns.Count, squad.Count);
        Assert.Equal(state.Map.PlayerSpawns, squad.Select(u => u.Position));
        Assert.Equal(squad, state.Units);
    }

    [Fact]
    public void DeployIsDeterministic()
    {
        Assert.Equal(Skirmish(7).StateHash(), Skirmish(7).StateHash());
    }

    [Fact]
    public void OverflowGoesToNearestFreeWalkableTilesNeverOccupied()
    {
        var map = Map(".....", "..#..", "P.#..");
        var state = NewState(map);
        var squad = DebugSquad.Create(state, Data, state.Rng, 5);
        SquadDeployer.Deploy(state, squad);

        Assert.Equal(new GridPos(0, 0), squad[0].Position);
        Assert.Equal(new GridPos(1, 0), squad[1].Position); // nearest, ties by Y then X
        Assert.Equal(new GridPos(0, 1), squad[2].Position);
        Assert.Equal(5, squad.Select(u => u.Position).Distinct().Count());
        Assert.All(squad, u => Assert.True(map.IsWalkable(u.Position)));
    }

    [Fact]
    public void DeployWithoutSpawnThrows()
    {
        var state = NewState(Map("...."));
        var squad = DebugSquad.Create(state, Data, state.Rng, 1);
        Assert.Throws<InvalidOperationException>(() => SquadDeployer.Deploy(state, squad));
    }

    [Fact]
    public void PodSpawnerRespectsPodSizes()
    {
        var state = NewState(Yard());
        var pods = PodSpawner.Spawn(state, Data, YardPods, state.Rng);

        Assert.Equal(3, pods.Count);
        for (var i = 0; i < pods.Count; i++)
            Assert.Equal(YardPods[i].EnemyIds.Count, state.Units.Count(u => u.Pod == pods[i].Id));
        Assert.Equal(YardPods.Sum(p => p.EnemyIds.Count), state.Units.Count);
    }

    [Fact]
    public void PodsSpawnOnDistinctEnemySpawnsWithFirstMemberOnIt()
    {
        var state = NewState(Yard());
        var pods = PodSpawner.Spawn(state, Data, YardPods, state.Rng);

        Assert.Equal(3, pods.Select(p => p.Spawn).Distinct().Count());
        Assert.All(pods, p => Assert.Contains(p.Spawn, state.Map.EnemySpawns));
        Assert.All(
            pods,
            p => Assert.Equal(p.Spawn, state.Units.First(u => u.Pod == p.Id).Position)
        );
    }

    [Fact]
    public void PodMembersNeverOverlapAndStandOnWalkableTiles()
    {
        var state = Skirmish(3);
        var positions = state.Units.Select(u => u.Position).ToList();
        Assert.Equal(positions.Count, positions.Distinct().Count());
        Assert.All(positions, p => Assert.True(state.Map.IsWalkable(p)));
    }

    [Fact]
    public void PodMembersAreInactiveEnemiesBuiltFromTheirDef()
    {
        var state = NewState(Yard());
        PodSpawner.Spawn(state, Data, YardPods, state.Rng);
        var warden = state.Units.First(u => u.DefId == "warden");
        var def = Data.Enemies["warden"];
        Assert.Equal(Faction.Meridian, warden.Faction);
        Assert.False(warden.IsActive);
        Assert.Equal(def.Hp, warden.Hp);
        Assert.Equal(def.Sprite.Set, warden.AppearanceId);
        Assert.Contains("shoot", warden.Abilities);
        Assert.Equal(state.Units.Count, state.Units.Select(u => u.DisplayName).Distinct().Count());
    }

    [Fact]
    public void TooManyPodsThrows()
    {
        var state = NewState(Map("e.", ".."));
        Assert.Throws<InvalidOperationException>(() =>
            PodSpawner.Spawn(state, Data, [Pod("a", "husk"), Pod("b", "husk")], state.Rng)
        );
    }

    [Fact]
    public void StateHashIsStableAcrossRuns()
    {
        Assert.Equal(Skirmish(11).StateHash(), Skirmish(11).StateHash());
        Assert.NotEqual(Skirmish(11).StateHash(), Skirmish(12).StateHash());
    }

    public static IEnumerable<object[]> Mutations() =>
        new (string Name, Action<TacticalState> Mutate)[]
        {
            ("position", s => s.Units[0].Position = new GridPos(0, 0)),
            ("hp", s => s.Units[0].Hp--),
            ("maxHp", s => s.Units[0].MaxHp++),
            ("aim", s => s.Units[0].Aim++),
            ("mobility", s => s.Units[0].Mobility++),
            ("defense", s => s.Units[0].Defense++),
            ("armor", s => s.Units[0].Armor++),
            ("sight", s => s.Units[0].Sight++),
            ("ap", s => s.Units[0].ActionPoints--),
            ("moved", s => s.Units[0].MovedThisTurn = true),
            ("ammo", s => s.Units[0].Weapon!.Ammo--),
            ("item", s => s.Units[0].Items.Add(new ItemState("frag", 1))),
            (
                "itemCharges",
                s =>
                {
                    s.Units[0].Items.Add(new ItemState("frag", 1));
                    s.Units[0].Items[0].Charges = 0;
                }
            ),
            ("ability", s => s.Units[0].Abilities.Add("sentinel")),
            ("cooldown", s => s.Units[0].Cooldowns["rally"] = 2),
            ("status", s => s.Units[0].Statuses.Set(StatusFlags.Overwatch)),
            ("bleed", s => s.Units[0].Statuses.BleedingOutTurnsLeft = 3),
            ("alive", s => s.Units[0].IsAlive = false),
            ("active", s => s.Units[^1].IsActive = true),
            ("pod", s => s.Units[^1].Pod = new PodId(99)),
            ("appearance", s => s.Units[0].AppearanceId = "other"),
            ("name", s => s.Units[0].DisplayName += "x"),
            ("turn", s => s.TurnNumber++),
            ("phase", s => s.Phase = TacticalPhase.Enemy),
            ("objective", s => s.Objective.Progress++),
            ("tile", s => s.Map.SetTile(new GridPos(0, 0), TileType.Wall)),
            ("rng", s => s.Rng.Next(0, 10)),
        }.Select(m => new object[] { m.Name, m.Mutate });

    [Theory]
    [MemberData(nameof(Mutations))]
    public void StateHashChangesWhenAnyFieldChanges(string name, Action<TacticalState> mutate)
    {
        var state = Skirmish(5);
        var before = state.StateHash();
        mutate(state);
        Assert.NotEqual(before, state.StateHash());
        Assert.True(name.Length > 0);
    }
}
