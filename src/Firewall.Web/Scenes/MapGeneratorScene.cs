using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Yaeger.Graphics;
using Yaeger.Input;
using Yaeger.Platform;

namespace Firewall.Web.Scenes;

/// <summary>
/// Debug scene for reviewing procedural maps: pick a biome, grid size, mission, pod count and
/// seed, regenerate with a button, and look at walkability, spawns and reachability overlays.
/// Keys: B biome, Z size, M mission, V pods, comma/period seed -/+, R new seed, 1-3 overlays,
/// 4 grid, Tab hides the panel, Esc goes back.
/// </summary>
public sealed class MapGeneratorScene : SceneBase
{
    private static readonly (int X, int Y)[] Sizes = [(3, 3), (4, 3), (3, 4), (4, 4)];
    private static readonly Biome[] Biomes = [Biome.Industrial, Biome.Urban, Biome.Suburban];
    private static readonly int[] PodCounts = [3, 4, 5];

    private readonly TacticalCamera _camera = new();
    private readonly List<string> _log = [];
    private TacticalCameraController _controller = null!;
    private MapGenerator _generator = null!;
    private GeneratedMap _result = null!;
    private MapTilemaps? _tilemaps;
    private bool[] _reached = [];
    private ulong _seed;
    private int _biome = 1;
    private int _size;
    private int _mission = (int)GenMission.Sabotage;
    private int _pods = 1;
    private bool _dirty = true;
    private bool _walkable;
    private bool _spawns = true;
    private bool _reachability = true;
    private bool _grid;
    private bool _panel = true;

    public override string Name => "Map Generator";

    private TacticalMap Map => _result.Map;

    protected override Camera2D Camera => _camera.ToCamera2D();

    protected override void OnEnter()
    {
        _controller = new TacticalCameraController(_camera, Ctx.Input, Ctx.Bindings);
        _controller.SetZoom(TacticalCameraController.MinZoom);
        if (Ctx.MapSources.Count == 0)
            throw new InvalidOperationException("No maps were loaded; the fallback needs one.");
        _generator = new MapGenerator(
            Ctx.Blocks,
            Ctx.Data.Tiles,
            () => Ctx.Data.LoadMap(Ctx.MapSources[0].Json),
            _log.Add
        );
        _seed = Ctx.Seeds.Next();
        Rebuild();
    }

    protected override void OnExit() => _tilemaps?.Dispose();

    private MapGenParams Params =>
        new()
        {
            Biome = Biomes[_biome],
            BlocksX = Sizes[_size].X,
            BlocksY = Sizes[_size].Y,
            Mission = (GenMission)_mission,
            PodCount = PodCounts[_pods],
        };

    private void Rebuild()
    {
        _dirty = false;
        _log.Clear();
        _result = _generator.GenerateDetailed(Params, new Pcg32Random(_seed));
        _reached =
            Map.PlayerSpawns.Count > 0
                ? MapGenerator.ReachableFrom(Map, Map.PlayerSpawns[0])
                : new bool[Map.Width * Map.Height];
        _tilemaps?.Dispose();
        _tilemaps = MapTilemaps.Build(World, Map, Ctx.Assets);
        _controller.MapSize = new Vector2(Map.Width, Map.Height);
        _camera.Center = new Vector2(Map.Width / 2f, Map.Height / 2f);
    }

    private void Cycle(ref int index, int count)
    {
        index = (index + 1) % count;
        _dirty = true;
    }

    private void NewSeed()
    {
        _seed = Ctx.Seeds.Next();
        _dirty = true;
    }

    protected override void OnUpdate(float dt)
    {
        if (Ctx.Bindings.WasPressed(GameAction.Cancel))
        {
            Ctx.Scenes.Pop();
            return;
        }
        var input = Ctx.Input;
        if (input.WasKeyPressed(Keys.B))
            Cycle(ref _biome, Biomes.Length);
        if (input.WasKeyPressed(Keys.Z))
            Cycle(ref _size, Sizes.Length);
        if (input.WasKeyPressed(Keys.M))
            Cycle(ref _mission, Enum.GetValues<GenMission>().Length);
        if (input.WasKeyPressed(Keys.V))
            Cycle(ref _pods, PodCounts.Length);
        if (input.WasKeyPressed(Keys.R))
            NewSeed();
        if (input.WasKeyPressed(Keys.Comma))
        {
            _seed--;
            _dirty = true;
        }
        if (input.WasKeyPressed(Keys.Period))
        {
            _seed++;
            _dirty = true;
        }
        if (input.WasKeyPressed(Keys.Num1))
            _walkable = !_walkable;
        if (input.WasKeyPressed(Keys.Num2))
            _spawns = !_spawns;
        if (input.WasKeyPressed(Keys.Num3))
            _reachability = !_reachability;
        if (input.WasKeyPressed(Keys.Num4))
            _grid = !_grid;
        if (input.WasKeyPressed(Keys.Tab))
            _panel = !_panel;

        if (_dirty)
            Rebuild();
        _controller.Update(dt, Ctx.Render.CanvasSize, Ctx.Ui.PointerOverUi);
    }

    protected override void DrawWorld(WorldOverlay overlay)
    {
        var map = Map;
        var walk = new Vector4(0.1f, 0.9f, 0.2f, 0.22f);
        var blocked = new Vector4(0.95f, 0.15f, 0.15f, 0.30f);
        var reachable = new Vector4(0.3f, 0.8f, 1f, 0.10f);
        var cut = new Vector4(1f, 0.1f, 0.9f, 0.7f);

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var pos = new GridPos(x, y);
            if (_walkable)
                overlay.FillTile(x, y, map.IsWalkable(pos) ? walk : blocked);
            if (_reachability && map.IsWalkable(pos))
                overlay.FillTile(x, y, _reached[y * map.Width + x] ? reachable : cut);
        }

        if (_spawns)
        {
            foreach (var t in map.PlayerSpawns)
                Marker(overlay, t, new Vector4(0.2f, 1f, 0.3f, 0.85f));
            foreach (var t in map.EvacTiles)
                Marker(overlay, t, new Vector4(0.3f, 0.5f, 1f, 0.6f));
            foreach (var t in map.EnemySpawns)
                Marker(overlay, t, new Vector4(1f, 0.2f, 0.2f, 0.9f));
            foreach (var t in map.Objectives)
                Marker(overlay, t, new Vector4(1f, 0.9f, 0.2f, 0.9f));
            foreach (var t in _result.CivilianSpots)
                Marker(overlay, t, new Vector4(1f, 0.6f, 0.9f, 0.9f));
        }

        if (_grid)
        {
            var line = new Vector4(1f, 1f, 1f, 0.25f);
            const float t = 0.02f;
            for (var x = 0; x <= map.Width; x++)
                overlay.FillRect(new Vector2(x - t / 2f, 0f), new Vector2(t, map.Height), line);
            for (var y = 0; y <= map.Height; y++)
                overlay.FillRect(new Vector2(0f, y - t / 2f), new Vector2(map.Width, t), line);
        }
    }

    private static void Marker(WorldOverlay overlay, GridPos pos, Vector4 color)
    {
        overlay.FillRect(new Vector2(pos.X + 0.2f, pos.Y + 0.2f), new Vector2(0.6f, 0.6f), color);
        overlay.OutlineTile(pos.X, pos.Y, 0.08f, color with { W = 1f });
    }

    protected override void DrawScreen(ScreenCanvas canvas, UiContext ui)
    {
        if (!_panel)
        {
            ui.Label(new UiRect(16, 16, 300, 20), "Tab: show panel", ui.Theme.Small);
            return;
        }
        var body = ui.Panel(new UiRect(16, 16, 330, 560), "Map Generator");
        var y = body.Y;
        const float h = 34f;

        if (ui.Button(new UiRect(body.X, y, body.W, h), $"Biome: {Biomes[_biome]}  (B)"))
            Cycle(ref _biome, Biomes.Length);
        y += h + 4;
        if (
            ui.Button(
                new UiRect(body.X, y, body.W, h),
                $"Size: {Sizes[_size].X}x{Sizes[_size].Y} blocks  (Z)"
            )
        )
            Cycle(ref _size, Sizes.Length);
        y += h + 4;
        if (ui.Button(new UiRect(body.X, y, body.W, h), $"Mission: {(GenMission)_mission}  (M)"))
            Cycle(ref _mission, Enum.GetValues<GenMission>().Length);
        y += h + 4;
        if (ui.Button(new UiRect(body.X, y, body.W, h), $"Pods: {PodCounts[_pods]}  (V)"))
            Cycle(ref _pods, PodCounts.Length);
        y += h + 4;
        var half = (body.W - 4) / 2f;
        if (ui.Button(new UiRect(body.X, y, half, h), "Seed -  ( , )"))
        {
            _seed--;
            _dirty = true;
        }
        if (ui.Button(new UiRect(body.X + half + 4, y, half, h), "Seed +  ( . )"))
        {
            _seed++;
            _dirty = true;
        }
        y += h + 4;
        if (
            ui.Button(
                new UiRect(body.X, y, body.W, h),
                "Regenerate: new seed  (R)",
                ButtonStyle.Primary
            )
        )
            NewSeed();
        y += h + 12;

        var map = Map;
        var walkableTiles = 0;
        var cutOff = 0;
        for (var i = 0; i < _reached.Length; i++)
        {
            var pos = new GridPos(i % map.Width, i / map.Width);
            if (!map.IsWalkable(pos))
                continue;
            walkableTiles++;
            if (!_reached[i])
                cutOff++;
        }
        var info = new[]
        {
            $"seed {_seed}   hash {(uint)MapHash.Of(map):x8}",
            $"{map.Width}x{map.Height} tiles, {walkableTiles} walkable, {cutOff} cut off",
            _result.FellBack
                ? $"FELL BACK to '{map.Id}' after {_result.Attempts} tries"
                : $"layout found in {_result.Attempts} {(_result.Attempts == 1 ? "try" : "tries")}",
            $"P {map.PlayerSpawns.Count}  E {map.EvacTiles.Count}  e {map.EnemySpawns.Count}  O {map.Objectives.Count}  civilians {_result.CivilianSpots.Count}",
        };
        foreach (var line in info)
        {
            ui.Label(new UiRect(body.X, y, body.W, 20), line, ui.Theme.Small);
            y += 22;
        }
        foreach (var line in _log)
        {
            ui.Label(new UiRect(body.X, y, body.W, 40), line, ui.Theme.Small, wrap: true);
            y += 42;
        }

        y += 8;
        string On(bool b) => b ? "on" : "off";
        var keys = new[]
        {
            $"1 walkable {On(_walkable)}   2 spawns {On(_spawns)}",
            $"3 reachability {On(_reachability)}   4 grid {On(_grid)}",
            "green P  blue E  red e  yellow O  pink civilian",
            "magenta: walkable but cut off from the first P",
            $"zoom {_controller.ZoomFactor:0.00}x   Tab: hide panel   Esc: back",
        };
        foreach (var line in keys)
        {
            ui.Label(new UiRect(body.X, y, body.W, 20), line, ui.Theme.Small);
            y += 22;
        }
    }
}
