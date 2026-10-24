using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;

namespace Firewall.Rules.Tests;

public class MapGeneratorTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly GameData Data = GameData.Load(f =>
        File.ReadAllText(Path.Combine(DataDir, f))
    );

    private static readonly BlockLibrary Blocks = BlockLibrary.Load(
        f => File.ReadAllText(Path.Combine(DataDir, f)),
        GameDataFiles.Blocks
    );

    private static readonly string[] Sample =
    [
        "abcdefghij",
        "0123456789",
        "ABCDEFGHIJ",
        "klmnopqrst",
        "KLMNOPQRST",
        "uvwxyz!@#$",
        "UVWXYZ%^&*",
        "((((()))))",
        "-=_+[]{};:",
        "<>?/|~`',.",
    ];

    private static MapGenerator Generator(List<string>? log = null) =>
        new(
            Blocks,
            Data.Tiles,
            () => Data.LoadMap(File.ReadAllText(Path.Combine(DataDir, "maps/test-yard.json"))),
            log is null ? null : log.Add
        );

    private static string Join(IReadOnlyList<string> rows) => string.Join('\n', rows);

    // --- transforms -------------------------------------------------------------------------

    [Fact]
    public void RotatingClockwiseMovesTheTopRowToTheRight()
    {
        var rows = new[] { "ab.", "cd.", "ef." };
        Assert.Equal(["eca", "fdb", "..."], BlockTransform.RotateClockwise(rows));
    }

    [Fact]
    public void RotatingAFlatBlockSwapsItsSides()
    {
        Assert.Equal(["da", "eb", "fc"], BlockTransform.RotateClockwise(["abc", "def"]));
    }

    [Fact]
    public void MirroringFlipsLeftToRight()
    {
        Assert.Equal(["cba", "fed"], BlockTransform.Mirror(["abc", "def"]));
    }

    [Fact]
    public void FourRotationsGiveTheIdentity()
    {
        IReadOnlyList<string> rows = Sample;
        for (var i = 0; i < 4; i++)
            rows = BlockTransform.RotateClockwise(rows);
        Assert.Equal(Sample, rows);
        Assert.NotEqual(Join(Sample), Join(BlockTransform.RotateClockwise(Sample)));
    }

    [Fact]
    public void MirroringTwiceGivesTheIdentity()
    {
        Assert.Equal(Sample, BlockTransform.Mirror(BlockTransform.Mirror(Sample)));
        Assert.NotEqual(Join(Sample), Join(BlockTransform.Mirror(Sample)));
    }

    [Fact]
    public void ApplyMirrorsThenRotates()
    {
        Assert.Equal(
            BlockTransform.RotateClockwise(BlockTransform.Mirror(Sample)),
            BlockTransform.Apply(Sample, 1, true)
        );
        Assert.Equal(Sample, BlockTransform.Apply(Sample, 4, false));
        Assert.Equal(Sample, BlockTransform.Apply(Sample, 0, false));
    }

    [Fact]
    public void EightTransformsOfAnAsymmetricBlockAreDistinct()
    {
        var variants = new HashSet<string>();
        for (var turns = 0; turns < 4; turns++)
            foreach (var mirror in new[] { false, true })
                variants.Add(Join(BlockTransform.Apply(Sample, turns, mirror)));
        Assert.Equal(8, variants.Count);
    }

    // --- authored blocks --------------------------------------------------------------------

    [Fact]
    public void ShippedBlocksAreListed()
    {
        var onDisk = Directory
            .GetFiles(Path.Combine(DataDir, "blocks"), "*.json", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(DataDir, f).Replace('\\', '/'))
            .Order();
        Assert.Equal(onDisk, GameDataFiles.Blocks.Order());
    }

    [Fact]
    public void EveryBlockParsesAndHasWalkableBorders()
    {
        Assert.Empty(Blocks.Validate(Data.Tiles));
    }

    [Fact]
    public void EveryBlockSitsInItsBiomeFolderAndIsNamedAfterItsId()
    {
        foreach (var file in GameDataFiles.Blocks)
        {
            var block = Blocks.All.Single(b =>
                file == $"blocks/{b.Biome.ToString().ToLowerInvariant()}/{b.Id}.json"
            );
            Assert.NotNull(block);
        }
        Assert.Equal(GameDataFiles.Blocks.Count, Blocks.All.Count);
    }

    [Theory]
    [InlineData(Biome.Industrial)]
    [InlineData(Biome.Urban)]
    [InlineData(Biome.Suburban)]
    public void EachBiomeHasTheMinimumContent(Biome biome)
    {
        Assert.True(Blocks.Find(biome, BlockTag.SpawnPlayer).Count >= 2);
        Assert.True(Blocks.Find(biome, BlockTag.Objective).Count >= 2);
        Assert.True(Blocks.Find(biome, BlockTag.Generic).Count >= 6);
    }

    [Fact]
    public void EveryBlockSurvivesAllTransformsWithItsBorderIntact()
    {
        foreach (var block in Blocks.All)
            for (var turns = 0; turns < 4; turns++)
                foreach (var mirror in new[] { false, true })
                {
                    var rows = BlockTransform.Apply(block.Rows, turns, mirror);
                    Assert.Equal(BlockLibrary.BlockSize, rows.Count);
                    Assert.All(rows, r => Assert.Equal(BlockLibrary.BlockSize, r.Length));
                    Assert.Equal(
                        block.Rows.Sum(r => r.Count(c => c == 'O')),
                        rows.Sum(r => r.Count(c => c == 'O'))
                    );
                }
    }

    [Fact]
    public void ValidationReportsBrokenBlocks()
    {
        var good = Blocks.Find(Biome.Industrial, BlockTag.Generic)[0];
        var walled = good with
        {
            Id = "walled",
            Rows = [.. good.Rows.Select(r => new string('#', r.Length))],
        };
        var noSpawn = good with { Id = "no-spawn", Tags = [BlockTag.SpawnPlayer] };
        var short_ = good with { Id = "short", Rows = [.. good.Rows.Take(9)] };
        var odd = good with { Id = "odd", Rows = [.. good.Rows.Select(r => r.Replace('.', '?'))] };
        var sealed_ = good with
        {
            Id = "sealed",
            Tags = [BlockTag.Objective],
            Rows =
            [
                "..........",
                ".########.",
                ".#......#.",
                ".#..O...#.",
                ".#......#.",
                ".#......#.",
                ".#......#.",
                ".########.",
                "..........",
                "..........",
            ],
        };
        var split = good with
        {
            Id = "split",
            Rows = [.. good.Rows.Select(r => r[..5] + '#' + r[6..])],
        };
        var errors = new BlockLibrary([
            walled,
            noSpawn,
            short_,
            odd,
            sealed_,
            split,
            good,
            good,
        ]).Validate(Data.Tiles);
        Assert.Contains(errors, e => e.Contains("'walled'") && e.Contains("top edge has 0"));
        Assert.Contains(errors, e => e.Contains("'no-spawn'") && e.Contains("no 'P'"));
        Assert.Contains(errors, e => e.Contains("'short'") && e.Contains("rows must be"));
        Assert.Contains(errors, e => e.Contains("'odd'") && e.Contains("unknown glyph"));
        Assert.Contains(
            errors,
            e => e.Contains("'sealed'") && e.Contains("no reachable neighbour")
        );
        Assert.Contains(errors, e => e.Contains("'split'") && e.Contains("separate regions"));
        Assert.Contains(errors, e => e.Contains($"'{good.Id}'") && e.Contains("duplicate"));
    }

    // --- generator --------------------------------------------------------------------------

    private static MapGenParams Params(Biome biome, GenMission mission = GenMission.Sabotage) =>
        new()
        {
            Biome = biome,
            Mission = mission,
            PodCount = 4,
        };

    [Theory]
    [InlineData(Biome.Industrial)]
    [InlineData(Biome.Urban)]
    [InlineData(Biome.Suburban)]
    public void SameSeedAndParamsGiveTheSameMap(Biome biome)
    {
        var p = Params(biome);
        var a = Generator().Generate(p, new Pcg32Random(1234));
        var b = Generator().Generate(p, new Pcg32Random(1234));
        Assert.Equal(MapHash.Of(a), MapHash.Of(b));
        Assert.Equal(a.EnemySpawns, b.EnemySpawns);

        var other = Generator().Generate(p, new Pcg32Random(4321));
        Assert.NotEqual(MapHash.Of(a), MapHash.Of(other));
    }

    [Fact]
    public void SeedsProduceVariedMaps()
    {
        var hashes = Enumerable
            .Range(0, 50)
            .Select(i =>
                MapHash.Of(Generator().Generate(Params(Biome.Urban), new Pcg32Random((ulong)i)))
            )
            .ToHashSet();
        Assert.True(hashes.Count >= 45);
    }

    [Theory]
    [InlineData(Biome.Industrial)]
    [InlineData(Biome.Urban)]
    [InlineData(Biome.Suburban)]
    public void TwoHundredSeedsAllValidateWithoutFallback(Biome biome)
    {
        var log = new List<string>();
        var generator = Generator(log);
        for (var seed = 0; seed < 200; seed++)
        {
            var p = (seed % 4) switch
            {
                0 => Params(biome),
                1 => Params(biome, GenMission.Rescue) with { BlocksX = 4, BlocksY = 3 },
                2 => Params(biome, GenMission.Purge) with { PodCount = 3 },
                _ => Params(biome, GenMission.Sabotage) with { BlocksX = 4, BlocksY = 4 },
            };
            var result = generator.GenerateDetailed(p, new Pcg32Random((ulong)seed));
            Assert.False(result.FellBack, $"{biome} seed {seed} fell back");
            AssertWellFormed(result, p, seed);
        }
        Assert.Empty(log);
    }

    private static void AssertWellFormed(GeneratedMap result, MapGenParams p, int seed)
    {
        var map = result.Map;
        var label = $"{p.Biome} seed {seed}";
        Assert.Equal(p.Biome, map.Biome);
        Assert.True(map.Width >= p.BlocksX * 10 + 2, label);
        Assert.True(map.Height >= p.BlocksY * 10 + 2, label);

        // The 1-tile wall border.
        for (var x = 0; x < map.Width; x++)
        {
            Assert.Equal(TileType.Wall, map[new GridPos(x, 0)]);
            Assert.Equal(TileType.Wall, map[new GridPos(x, map.Height - 1)]);
        }
        for (var y = 0; y < map.Height; y++)
        {
            Assert.Equal(TileType.Wall, map[new GridPos(0, y)]);
            Assert.Equal(TileType.Wall, map[new GridPos(map.Width - 1, y)]);
        }

        Assert.NotEmpty(map.PlayerSpawns);
        Assert.NotEmpty(map.EvacTiles);
        Assert.Equal(p.PodCount, map.EnemySpawns.Count);
        Assert.Equal(p.PodCount, map.EnemySpawns.Distinct().Count());
        // Mutual reachability of P, E, e, O and civilians.
        var reach = MapGenerator.ReachableFrom(map, map.PlayerSpawns[0]);
        bool Reached(GridPos t) => reach[t.Y * map.Width + t.X];
        for (var i = 0; i < reach.Length; i++)
            Assert.True(
                reach[i] || !map.IsWalkable(new GridPos(i % map.Width, i / map.Width)),
                $"{label}: tile {i % map.Width},{i / map.Width} is cut off"
            );
        Assert.All(map.PlayerSpawns, t => Assert.True(Reached(t), label));
        Assert.All(map.EvacTiles, t => Assert.True(Reached(t), label));
        Assert.All(map.EnemySpawns, t => Assert.True(Reached(t), label));
        Assert.All(result.CivilianSpots, t => Assert.True(Reached(t), label));
        foreach (var o in map.Objectives)
            Assert.Contains(
                new[] { (1, 0), (-1, 0), (0, 1), (0, -1) },
                d =>
                    map.InBounds(new GridPos(o.X + d.Item1, o.Y + d.Item2))
                    && Reached(new GridPos(o.X + d.Item1, o.Y + d.Item2))
            );

        switch (p.Mission)
        {
            case GenMission.Sabotage:
                Assert.Single(map.Objectives);
                break;
            case GenMission.Rescue:
                Assert.Equal(2, result.CivilianSpots.Count);
                Assert.Empty(map.Objectives);
                break;
            default:
                Assert.Empty(map.Objectives);
                Assert.Empty(result.CivilianSpots);
                break;
        }

        // Spawns keep their distance from the squad and from each other.
        var distance = WalkingDistances(map, map.PlayerSpawns);
        foreach (var s in map.EnemySpawns)
            Assert.True(
                distance[s.Y * map.Width + s.X] >= MapGenerator.MinSpawnDistance,
                $"{label}: spawn {s} is {distance[s.Y * map.Width + s.X]} tiles from the squad"
            );
        for (var i = 0; i < map.EnemySpawns.Count; i++)
        for (var j = i + 1; j < map.EnemySpawns.Count; j++)
            Assert.True(
                map.EnemySpawns[i].DistanceTo(map.EnemySpawns[j]) >= MapGenerator.MinPodSpacing,
                $"{label}: spawns {map.EnemySpawns[i]} and {map.EnemySpawns[j]} are too close"
            );
    }

    /// <summary>Independent Dijkstra (orthogonal 1, diagonal 1.5, no corner cutting) in tiles.</summary>
    private static double[] WalkingDistances(TacticalMap map, IEnumerable<GridPos> sources)
    {
        var dist = new double[map.Width * map.Height];
        Array.Fill(dist, double.PositiveInfinity);
        var queue = new PriorityQueue<GridPos, double>();
        foreach (var s in sources)
        {
            dist[s.Y * map.Width + s.X] = 0;
            queue.Enqueue(s, 0);
        }
        while (queue.TryDequeue(out var pos, out var d))
        {
            if (d > dist[pos.Y * map.Width + pos.X])
                continue;
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                var next = new GridPos(pos.X + dx, pos.Y + dy);
                if (!map.IsWalkable(next))
                    continue;
                var diagonal = dx != 0 && dy != 0;
                if (
                    diagonal
                    && (
                        !map.IsWalkable(new GridPos(next.X, pos.Y))
                        || !map.IsWalkable(new GridPos(pos.X, next.Y))
                    )
                )
                    continue;
                var total = d + (diagonal ? 1.5 : 1.0);
                if (total < dist[next.Y * map.Width + next.X])
                {
                    dist[next.Y * map.Width + next.X] = total;
                    queue.Enqueue(next, total);
                }
            }
        }
        return dist;
    }

    [Fact]
    public void ObjectiveBlocksStayTwoBlocksFromThePlayerBlock()
    {
        // 3x3, no roads possible to confuse the maths: industrial with a seed sweep, checked via
        // the evac zone (inside the player block) and the relay.
        var generator = Generator();
        for (var seed = 0; seed < 100; seed++)
        {
            var map = generator.Generate(Params(Biome.Industrial), new Pcg32Random((ulong)seed));
            var relay = map.Objectives[0];
            var nearest = map.PlayerSpawns.Min(p =>
                Math.Abs(p.X - relay.X) + Math.Abs(p.Y - relay.Y)
            );
            Assert.True(
                nearest >= 11,
                $"seed {seed}: relay is only {nearest} tiles from the squad"
            );
        }
    }

    [Fact]
    public void RoadsAppearOftenInUrbanAndRarelyInIndustrialMaps()
    {
        int WithRoad(Biome biome) =>
            Enumerable
                .Range(0, 100)
                .Count(seed =>
                {
                    var map = Generator().Generate(Params(biome), new Pcg32Random((ulong)seed));
                    return map.Width > 32 || map.Height > 32;
                });
        Assert.Equal(100, WithRoad(Biome.Urban));
        var suburban = WithRoad(Biome.Suburban);
        Assert.InRange(suburban, 40, 100);
        Assert.InRange(WithRoad(Biome.Industrial), 1, 50);
    }

    [Fact]
    public void RoadStripsAreTwoTilesWide()
    {
        var map = Generator().Generate(Params(Biome.Urban), new Pcg32Random(7));
        Assert.Equal((34, 34), (map.Width, map.Height));
    }

    [Fact]
    public void FallsBackAndLogsWhenTheBlocksCannotWork()
    {
        // Every tile of every block is a wall, so nothing can ever validate.
        var walls = Blocks.All.Select(b =>
            b with
            {
                Rows = [.. b.Rows.Select(r => new string('#', r.Length))],
            }
        );
        var log = new List<string>();
        var generator = new MapGenerator(
            new BlockLibrary(walls),
            Data.Tiles,
            () => Data.LoadMap(File.ReadAllText(Path.Combine(DataDir, "maps/test-yard.json"))),
            log.Add
        );
        var result = generator.GenerateDetailed(Params(Biome.Urban), new Pcg32Random(1));
        Assert.True(result.FellBack);
        Assert.Equal(MapGenerator.MaxAttempts, result.Attempts);
        Assert.Equal("test-yard", result.Map.Id);
        Assert.Single(log);
    }

    [Fact]
    public void ThrowsWhenABiomeHasNoBlocks()
    {
        var generator = new MapGenerator(
            BlockLibrary.Empty,
            Data.Tiles,
            () => throw new InvalidOperationException()
        );
        Assert.Throws<InvalidOperationException>(() =>
            generator.Generate(Params(Biome.Urban), new Pcg32Random(1))
        );
    }

    [Fact]
    public void TheLargestGridNeverFallsBack()
    {
        var generator = Generator();
        foreach (var biome in new[] { Biome.Industrial, Biome.Urban, Biome.Suburban })
            for (var seed = 0; seed < 20; seed++)
            {
                var p = Params(biome, GenMission.Rescue) with { BlocksX = 4, BlocksY = 4 };
                var result = generator.GenerateDetailed(p, new Pcg32Random((ulong)(seed + 1000)));
                Assert.False(result.FellBack);
            }
    }

    [Fact]
    public void GenericBlocksAuthorEnemySpawnTiles()
    {
        foreach (var block in Blocks.All.Where(b => b.Tags.Contains(BlockTag.Generic)))
            Assert.True(
                block.Rows.Sum(r => r.Count(c => c == 'e')) >= 2,
                $"{block.Id} needs 'e' tiles"
            );
    }

    [Theory]
    [InlineData(Biome.Industrial)]
    [InlineData(Biome.Urban)]
    [InlineData(Biome.Suburban)]
    public void EnemySpawnsStayAwayFromTheBorderAndCorners(Biome biome)
    {
        var generator = Generator();
        for (var seed = 0; seed < 200; seed++)
        {
            var map = generator.Generate(Params(biome), new Pcg32Random((ulong)seed));
            foreach (var s in map.EnemySpawns)
                Assert.True(
                    s.X >= 3 && s.Y >= 3 && s.X < map.Width - 3 && s.Y < map.Height - 3,
                    $"{biome} seed {seed}: spawn {s} hugs the border"
                );
        }
    }

    [Fact]
    public void PlayerBlocksHoldASquadAndAnEvacZone()
    {
        foreach (var block in Blocks.All.Where(b => b.Tags.Contains(BlockTag.SpawnPlayer)))
        {
            var glyphs = block.Rows.SelectMany(r => r).ToList();
            Assert.True(glyphs.Count(c => c == 'P') >= 4, $"{block.Id} needs 4 P tiles");
            Assert.True(glyphs.Count(c => c == 'E') >= 2, $"{block.Id} needs an evac zone");
        }
    }
}
