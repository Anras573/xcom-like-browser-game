using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Tactical;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Yaeger.Graphics;
using Yaeger.Input;

namespace Firewall.Web.Scenes;

/// <summary>
/// Debug scene: shows any shipped map with the tilemap renderer and overlay toggles drawn in the
/// world pass (so they line up with the tiles at every zoom). 1-4 toggle overlays, Tab / Shift+Tab
/// switch map, X destroys the hovered destructible tile, Esc goes back.
/// </summary>
public sealed class MapViewerScene : SceneBase
{
    private readonly TacticalCamera _camera = new();
    private TacticalCameraController _controller = null!;
    private TacticalMap _map = null!;
    private MapTilemaps? _tilemaps;
    private int _mapIndex;
    private bool _walkable = true;
    private bool _cover = true;
    private bool _los;
    private bool _grid = true;
    private GridPos? _hover;

    public override string Name => "Map Viewer";

    protected override Camera2D Camera => _camera.ToCamera2D();

    protected override void OnEnter()
    {
        _controller = new TacticalCameraController(_camera, Ctx.Input, Ctx.Bindings);
        if (Ctx.MapSources.Count == 0)
            throw new InvalidOperationException("No maps were loaded.");
        LoadMap(0);
    }

    protected override void OnExit() => _tilemaps?.Dispose();

    private void LoadMap(int index)
    {
        _mapIndex = (index % Ctx.MapSources.Count + Ctx.MapSources.Count) % Ctx.MapSources.Count;
        _tilemaps?.Dispose();
        _map = Ctx.Data.LoadMap(Ctx.MapSources[_mapIndex].Json);
        _tilemaps = MapTilemaps.Build(World, _map, Ctx.Assets);
        _controller.MapSize = new Vector2(_map.Width, _map.Height);
        _camera.Center = new Vector2(_map.Width / 2f, _map.Height / 2f);
    }

    protected override void OnUpdate(float dt)
    {
        if (Ctx.Bindings.WasPressed(GameAction.Cancel))
        {
            Ctx.Scenes.Pop();
            return;
        }
        var input = Ctx.Input;
        if (input.WasKeyPressed(Keys.Num1))
            _walkable = !_walkable;
        if (input.WasKeyPressed(Keys.Num2))
            _cover = !_cover;
        if (input.WasKeyPressed(Keys.Num3))
            _los = !_los;
        if (input.WasKeyPressed(Keys.Num4))
            _grid = !_grid;
        if (input.WasKeyPressed(Keys.Tab))
            LoadMap(_mapIndex + (input.IsKeyPressed(Keys.Shift) ? -1 : 1));

        var canvas = Ctx.Render.CanvasSize;
        _controller.Update(dt, canvas, Ctx.Ui.PointerOverUi);
        _hover = Ctx.Ui.PointerOverUi ? null : PickTile(canvas);

        if (input.WasKeyPressed(Keys.X) && _hover is { } pos)
            if (_map.Def(_map[pos]).DestructibleInto is { } into)
                _map.SetTile(pos, into);
    }

    // Same view-projection the renderer uses, so the pick matches the pixels.
    private GridPos? PickTile(Vector2 canvas)
    {
        var mouse = Ctx.Input.MousePosition;
        if (
            !Ctx.Input.IsMouseInside
            || mouse.X < 0f
            || mouse.Y < 0f
            || mouse.X >= canvas.X
            || mouse.Y >= canvas.Y
        )
            return null;
        var vp = _camera.ToCamera2D().ViewProjection(canvas.X / MathF.Max(canvas.Y, 1f));
        var (x, y) = Picking.WorldToTile(
            Picking.ScreenToWorld(Picking.CanvasToNdc(mouse, canvas), vp)
        );
        var pos = new GridPos(x, y);
        return _map.InBounds(pos) ? pos : null;
    }

    protected override void DrawWorld(WorldOverlay overlay)
    {
        var walk = new Vector4(0.1f, 0.9f, 0.2f, 0.22f);
        var blocked = new Vector4(0.95f, 0.15f, 0.15f, 0.30f);
        var coverColor = new Vector4(0.3f, 0.7f, 1f, 0.85f);
        var losColor = new Vector4(1f, 0.9f, 0.2f, 0.9f);

        for (var y = 0; y < _map.Height; y++)
        for (var x = 0; x < _map.Width; x++)
        {
            var pos = new GridPos(x, y);
            if (_walkable)
                overlay.FillTile(x, y, _map.IsWalkable(pos) ? walk : blocked);
            switch (_cover ? _map.CoverAt(pos) : CoverLevel.None)
            {
                case CoverLevel.Half: // bottom half of the tile
                    overlay.FillRect(
                        new Vector2(x + 0.25f, y + 0.2f),
                        new Vector2(0.5f, 0.3f),
                        coverColor
                    );
                    break;
                case CoverLevel.Full:
                    overlay.FillRect(
                        new Vector2(x + 0.25f, y + 0.2f),
                        new Vector2(0.5f, 0.6f),
                        coverColor
                    );
                    break;
            }
            if (_los && _map.BlocksLos(pos))
            {
                overlay.Line(
                    new Vector2(x + 0.1f, y + 0.1f),
                    new Vector2(x + 0.9f, y + 0.9f),
                    0.06f,
                    losColor
                );
                overlay.Line(
                    new Vector2(x + 0.1f, y + 0.9f),
                    new Vector2(x + 0.9f, y + 0.1f),
                    0.06f,
                    losColor
                );
            }
        }

        if (_grid)
        {
            var line = new Vector4(1f, 1f, 1f, 0.25f);
            const float t = 0.02f;
            for (var x = 0; x <= _map.Width; x++)
                overlay.FillRect(new Vector2(x - t / 2f, 0f), new Vector2(t, _map.Height), line);
            for (var y = 0; y <= _map.Height; y++)
                overlay.FillRect(new Vector2(0f, y - t / 2f), new Vector2(_map.Width, t), line);
        }

        if (_hover is { } h)
            overlay.OutlineTile(h.X, h.Y, 0.06f, new Vector4(1f, 0.9f, 0.3f, 1f));
    }

    protected override void DrawScreen(ScreenCanvas canvas, UiContext ui)
    {
        canvas.FillRect(new Vector2(16, 16), new Vector2(330, 196), new Vector4(0f, 0f, 0f, 0.6f));
        canvas.Text(
            "Map Viewer",
            new Vector2(28, 24),
            new TextStyle(TextStyles.FutureNarrow, 20, new Color(255, 220, 90))
        );
        var white = TextStyles.Small.With(Color.White);
        string On(bool b) => b ? "on" : "off";
        var lines = new[]
        {
            $"{_map.Name}  ({_map.Width}x{_map.Height}, {_map.Biome})",
            $"zoom  {_controller.ZoomFactor:0.00}x",
            _hover is { } p ? $"tile  {p.X}, {p.Y}  {_map[p]}  cover {_map.CoverAt(p)}" : "tile  -",
            $"1 walkable {On(_walkable)}   2 cover {On(_cover)}",
            $"3 LOS blockers {On(_los)}   4 grid {On(_grid)}",
            "Tab: next map   X: destroy hovered tile",
        };
        for (var i = 0; i < lines.Length; i++)
            canvas.Text(lines[i], new Vector2(28, 54 + i * 24), white);
        canvas.Text("Esc: back", new Vector2(16, 220), TextStyles.Small);
    }
}
