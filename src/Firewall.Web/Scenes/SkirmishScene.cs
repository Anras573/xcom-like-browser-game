using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Firewall.Web.Units;
using Yaeger.Graphics;
using Yaeger.Input;

namespace Firewall.Web.Scenes;

/// <summary>
/// "Skirmish (debug)": the test yard with a debug squad of four and three enemy pods. Click a
/// soldier or press Tab / Shift+Tab to select, F re-centres, Esc goes back.
/// </summary>
public sealed class SkirmishScene : SceneBase
{
    public const string MapId = "test-yard";

    private static readonly IReadOnlyList<PodSpec> Pods =
    [
        new("Husk Pack", ["husk", "husk", "husk"]),
        new("Warden Patrol", ["warden", "warden"]),
        new("Mixed Patrol", ["warden", "husk", "husk", "husk"]),
    ];

    private readonly TacticalCamera _camera = new();
    private readonly Dictionary<UnitId, UnitView> _views = [];
    private TacticalCameraController _controller = null!;
    private TacticalState _state = null!;
    private MapTilemaps? _tilemaps;
    private UnitId? _selected;
    private TacticalUnit? _hover;
    private float _time;

    public override string Name => "Skirmish";

    /// <summary>The soldier currently selected, if any.</summary>
    public TacticalUnit? Selected => _selected is { } id ? _state.Unit(id) : null;

    protected override Camera2D Camera => _camera.ToCamera2D();

    protected override void OnEnter()
    {
        var source = Ctx.MapSources.FirstOrDefault(m => m.Id == MapId);
        if (source.Json is null)
            throw new InvalidOperationException($"Map '{MapId}' was not loaded.");
        var map = Ctx.Data.LoadMap(source.Json);
        var rng = new Pcg32Random(Ctx.Seeds.Next());
        _state = new TacticalState(map, rng);
        SquadDeployer.Deploy(_state, DebugSquad.Create(_state, Ctx.Data, rng.Fork("squad")));
        PodSpawner.Spawn(_state, Ctx.Data, Pods, rng.Fork("pods"));

        _tilemaps = MapTilemaps.Build(World, map, Ctx.Assets);
        foreach (var unit in _state.Units)
            _views[unit.Id] = new UnitView(
                World,
                Ctx.Assets,
                Ctx.Data,
                unit,
                UnitPresentation.InitialFacing(unit, _state)
            );

        _controller = new TacticalCameraController(_camera, Ctx.Input, Ctx.Bindings)
        {
            MapSize = new Vector2(map.Width, map.Height),
        };
        _camera.Center = new Vector2(map.Width / 2f, map.Height / 2f);
        Select(Soldiers().First(), instant: true);
    }

    protected override void OnExit() => _tilemaps?.Dispose();

    private List<TacticalUnit> Soldiers() =>
        _state.Units.Where(u => u.Faction == Faction.Firewall && u.IsAlive).ToList();

    private void Select(TacticalUnit unit, bool instant = false)
    {
        _selected = unit.Id;
        _controller.FocusOn(
            TacticalCamera.TileCenter(unit.Position.X, unit.Position.Y),
            instant ? 0f : 0.35f
        );
    }

    protected override void OnUpdate(float dt)
    {
        _time += dt;
        if (Ctx.Bindings.WasPressed(GameAction.Cancel))
        {
            Ctx.Scenes.Pop();
            return;
        }

        var canvas = Ctx.Render.CanvasSize;
        _controller.Update(dt, canvas, Ctx.Ui.PointerOverUi);
        var tile = Ctx.Ui.PointerOverUi ? null : PickTile(canvas);
        _hover = tile is { } t ? _state.UnitAt(t) : null;

        var soldiers = Soldiers();
        if (soldiers.Count > 0)
        {
            if (Ctx.Bindings.WasPressed(GameAction.NextUnit))
                CycleSelection(soldiers, Ctx.Input.IsKeyPressed(Keys.Shift) ? -1 : 1);
            else if (Ctx.Bindings.WasPressed(GameAction.CenterCamera) && Selected is { } sel)
                Select(sel);
            else if (
                _hover is { Faction: Faction.Firewall } clicked
                && !Ctx.Ui.PointerOverUi
                && Ctx.Clicks.ConsumeClick()
            )
                Select(clicked);
        }

        foreach (var (id, view) in _views)
        {
            var unit = _state.Unit(id);
            if (!unit.IsAlive)
            {
                view.Destroy();
                _views.Remove(id);
                continue;
            }
            view.Sync(selected: id == _selected, showOverlay: ShowOverlay(unit), time: _time);
        }
    }

    // Soldiers always; enemies once fog of war can say they're visible (until then, all are).
    private static bool ShowOverlay(TacticalUnit unit) => unit.IsAlive;

    private void CycleSelection(List<TacticalUnit> soldiers, int step)
    {
        var current = soldiers.FindIndex(u => u.Id == _selected);
        var next = ((current + step) % soldiers.Count + soldiers.Count) % soldiers.Count;
        Select(soldiers[next]);
    }

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
        return _state.Map.InBounds(pos) ? pos : null;
    }

    protected override void DrawScreen(ScreenCanvas canvas, UiContext ui)
    {
        canvas.Text("Skirmish (debug)", new Vector2(16, 12), TextStyles.Heading);
        canvas.Text(
            "Click / Tab: select soldier   F: centre   Esc: back",
            new Vector2(16, 48),
            TextStyles.Small
        );
        if (Selected is { } sel)
            canvas.Text($"Selected: {sel.DisplayName}", new Vector2(16, 70), TextStyles.Body);

        if (_hover is { } unit)
            DrawTooltip(canvas, ui, unit);
    }

    private void DrawTooltip(ScreenCanvas canvas, UiContext ui, TacticalUnit unit)
    {
        var enemy = unit.Faction == Faction.Meridian ? Ctx.Data.Enemies[unit.DefId] : null;
        var lines = UnitPresentation.TooltipLines(unit, enemy);
        var style = TextStyles.Tooltip;
        var width = lines.Max(l => canvas.Measure(l, style)) + 20f;
        var height = lines.Count * 20f + 12f;
        var pos = ui.Mouse + new Vector2(18f, 18f);
        var canvasRect = ui.Space.CanvasRect;
        pos.X = MathF.Min(pos.X, canvasRect.Right - width - 4f);
        pos.Y = MathF.Min(pos.Y, canvasRect.Bottom - height - 4f);

        canvas.FillRect(pos, new Vector2(width, height), new Vector4(0.05f, 0.07f, 0.1f, 0.92f));
        for (var i = 0; i < lines.Count; i++)
            canvas.Text(
                lines[i],
                pos + new Vector2(10f, 6f + i * 20f),
                i == 0 ? TextStyles.Body : style
            );
    }
}
