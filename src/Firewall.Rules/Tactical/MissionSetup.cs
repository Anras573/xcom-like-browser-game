using Firewall.Rules.Data;
using Firewall.Rules.Rng;

namespace Firewall.Rules.Tactical;

/// <summary>Where <see cref="MissionSetup"/> gets maps from: the block library and the handcrafted maps by id.</summary>
public sealed record MissionMaps(BlockLibrary Blocks, Func<string, TacticalMap> Load)
{
    /// <summary>The handcrafted map used when generation fails and for story missions without their own.</summary>
    public const string FallbackId = "test-yard";
}

/// <summary>
/// Builds a tactical mission from a <see cref="MissionSpec"/> (GDD §4.10): map, squad, pods, the
/// objective and whatever the mission type adds (civilians, the Relay Node). The campaign and the
/// Skirmish menu both go through here, and the result is deterministic per seed.
/// </summary>
public static class MissionSetup
{
    /// <summary>Civilians in a Rescue mission.</summary>
    public const string CiviliansParameter = "civilians";

    public const string RequiredParameter = "required";

    /// <summary>Civilians stand at least this many tiles from the squad's spawn.</summary>
    public const string MinDistanceParameter = "minDistance";

    /// <summary>Civilians start at least this many tiles from every enemy.</summary>
    public const int MinEnemyDistance = 6;

    private const string TurnLimitParameter = "turnLimit";

    /// <summary>
    /// Sets the mission up. <paramref name="squad"/> is the soldiers' specs (null: four rookies).
    /// The state carries its objective in <see cref="TacticalState.Rule"/>.
    /// </summary>
    public static TacticalState Create(
        MissionSpec spec,
        IReadOnlyList<SoldierSpec>? squad,
        GameData data,
        IRandom rng,
        MissionMaps maps
    )
    {
        var id = spec.MissionId;
        var podCount = EncounterBuilder.PodCount(id, rng);
        var map = BuildMap(spec, data, rng, maps, podCount, out var civilianSpots);
        podCount = Math.Min(podCount, map.EnemySpawns.Count);

        var state = new TacticalState(map, rng, id)
        {
            PodsDriftToSquad = spec.Difficulty == Difficulty.Veteran,
        };
        var members = squad is null
            ? DebugSquad.Create(state, data, rng.Fork("squad"))
            : DebugSquad.Create(state, data, rng.Fork("squad"), squad);
        SquadDeployer.Deploy(state, members);

        var pods = EncounterBuilder.Build(
            data,
            spec.ForceLevel,
            podCount,
            rng.Fork("encounter"),
            id
        );
        PodSpawner.Spawn(state, data, pods, rng.Fork("pods"));

        state.Rule = CreateObjective(spec, data);
        switch (spec.Type)
        {
            case MissionType.Rescue:
                PlaceCivilians(state, data, rng.Fork("civilians"), civilianSpots, spec);
                break;
            case MissionType.Sabotage:
                PlaceRelay(state, data);
                break;
        }
        return state;
    }

    /// <summary>The goal of the mission type.</summary>
    public static IMissionObjective CreateObjective(MissionSpec spec, GameData data)
    {
        var parameters = data.Missions.TryGet(spec.MissionId, out var def)
            ? def.Parameters
            : new Dictionary<string, double>();
        return spec.Type switch
        {
            MissionType.Rescue => new RescueObjective(
                data,
                (int)parameters.GetValueOrDefault(RequiredParameter, 4)
            ),
            MissionType.Sabotage => new SabotageObjective(data, TurnLimit(spec, parameters)),
            MissionType.Capture => new CaptureObjective(data),
            _ => new PurgeObjective(data),
        };
    }

    /// <summary>T turns: 8, Recruit 10, Veteran 7.</summary>
    public static int TurnLimit(MissionSpec spec, IReadOnlyDictionary<string, double> parameters)
    {
        var key = spec.Difficulty switch
        {
            Difficulty.Recruit => TurnLimitParameter + "Recruit",
            Difficulty.Veteran => TurnLimitParameter + "Veteran",
            _ => TurnLimitParameter,
        };
        return (int)parameters.GetValueOrDefault(key, 8);
    }

    /// <summary>How difficulty bends the enemy AI (GDD §7).</summary>
    public static AiDifficulty AiDifficultyOf(Difficulty difficulty) =>
        difficulty switch
        {
            Difficulty.Recruit => AiDifficulty.Recruit,
            Difficulty.Veteran => AiDifficulty.Veteran,
            _ => AiDifficulty.Normal,
        };

    private static TacticalMap BuildMap(
        MissionSpec spec,
        GameData data,
        IRandom rng,
        MissionMaps maps,
        int podCount,
        out IReadOnlyList<GridPos> civilianSpots
    )
    {
        civilianSpots = [];
        if (spec.StoryMapId is { } story)
            return maps.Load(story);
        var generator = new MapGenerator(
            maps.Blocks,
            data.Tiles,
            () => maps.Load(MissionMaps.FallbackId)
        );
        var result = generator.GenerateDetailed(
            new MapGenParams
            {
                Biome = spec.Biome,
                Mission = spec.Type switch
                {
                    MissionType.Rescue => GenMission.Rescue,
                    MissionType.Sabotage => GenMission.Sabotage,
                    MissionType.Capture => GenMission.Capture,
                    _ => GenMission.Purge,
                },
                PodCount = podCount,
            },
            rng.Fork("map")
        );
        civilianSpots = result.CivilianSpots;
        return result.Map;
    }

    // The Relay Node stands on the objective tile(s); a map without one gets it far from the squad.
    private static void PlaceRelay(TacticalState state, GameData data)
    {
        if (state.Map.Objectives.Count > 0)
        {
            PodSpawner.SpawnObjects(state, data, SabotageObjective.RelayId);
            return;
        }
        var from = state.Map.PlayerSpawns[0];
        var tile = state
            .NearestFreeTiles(from)
            .Reverse()
            .First(t => state.Map.EnemySpawns.All(e => e != t));
        var def = data.Enemies[SabotageObjective.RelayId];
        var unit = PodSpawner.CreateUnit(state, def, def.Name);
        unit.Position = tile;
        state.AddUnit(unit);
    }

    /// <summary>
    /// Civilians (6 by default) in clusters of 1-2 on walkable tiles the squad can reach, at least
    /// 10 tiles from its spawn: the generator's objective spots first, then other floor.
    /// </summary>
    private static void PlaceCivilians(
        TacticalState state,
        GameData data,
        IRandom rng,
        IReadOnlyList<GridPos> spots,
        MissionSpec spec
    )
    {
        var parameters = data.Missions[spec.MissionId].Parameters;
        var total = (int)parameters.GetValueOrDefault(CiviliansParameter, 6);
        var minDistance = parameters.GetValueOrDefault(MinDistanceParameter, 10);
        var map = state.Map;
        var reach = MapGenerator.ReachableFrom(map, map.PlayerSpawns[0]);

        var enemies = state.Units.Where(u => u.Faction == Faction.Meridian).ToList();
        var look = rng.Fork("looks");
        var placed = 0;

        // Civilians start well away from the pods; a cramped map relaxes the gap rather than failing.
        foreach (var gap in new[] { MinEnemyDistance, MinEnemyDistance / 2, 0 })
        {
            bool Valid(GridPos t) =>
                map.InBounds(t)
                && map.IsWalkable(t)
                && reach[t.Y * map.Width + t.X]
                && !state.IsOccupied(t)
                && !map.EnemySpawns.Contains(t)
                && !map.EvacTiles.Contains(t)
                && map.PlayerSpawns.All(p => p.DistanceTo(t) >= minDistance)
                && enemies.All(e => e.Position.DistanceTo(t) >= gap);

            var anchors = Shuffle(spots.Where(Valid).ToList(), rng);
            var others = new List<GridPos>();
            for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
                if (new GridPos(x, y) is var t && Valid(t) && !anchors.Contains(t))
                    others.Add(t);
            anchors.AddRange(Shuffle(others, rng));

            foreach (var anchor in anchors)
            {
                if (placed >= total)
                    break;
                if (state.IsOccupied(anchor))
                    continue;
                Add(anchor);
                // Half the clusters hold a second civilian next to the first.
                if (placed < total && rng.Roll(50))
                {
                    var next = Neighbours(anchor).FirstOrDefault(n => Valid(n));
                    if (Valid(next))
                        Add(next);
                }
            }
            if (placed >= total)
                break;
        }
        if (placed < total)
            throw new InvalidOperationException(
                $"Map '{map.Id}' has room for {placed} of {total} civilians."
            );

        void Add(GridPos at)
        {
            var look1 = Civilians.Appearances[look.Next(0, Civilians.Appearances.Count)];
            var civilian = Civilians.Create(state, look1, $"Civilian {++placed}");
            civilian.Position = at;
            state.AddUnit(civilian);
        }
    }

    private static IEnumerable<GridPos> Neighbours(GridPos p)
    {
        yield return new GridPos(p.X + 1, p.Y);
        yield return new GridPos(p.X - 1, p.Y);
        yield return new GridPos(p.X, p.Y + 1);
        yield return new GridPos(p.X, p.Y - 1);
    }

    private static List<GridPos> Shuffle(List<GridPos> list, IRandom rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
