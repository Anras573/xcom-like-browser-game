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
    private TacticalEngine _engine = null!;
    private readonly Queue<PhaseStarted> _banners = new();
    private float _bannerTime;
    private float _exhaustedTimer = -1f;
    private MapTilemaps? _tilemaps;
    private CoverShieldsView _shields = null!;
    private UnitId? _selected;
    private MoveRangeOverlay? _range;
    private GridPos? _hoverTile;
    private readonly Queue<(UnitId Unit, MoveTween Tween)> _moves = new();
    private (UnitId Unit, MoveTween Tween, float Elapsed)? _moving;
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
        _engine = new TacticalEngine(_state, Ctx.Data);

        map.TileChanged += _ => InvalidateRange();
        _tilemaps = MapTilemaps.Build(World, map, Ctx.Assets);
        _shields = new CoverShieldsView(World, Ctx.Assets);
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
        InvalidateRange();
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
        _hoverTile = tile;
        _hover = tile is { } t ? _state.UnitAt(t) : null;
        UpdateMoveAnimation(dt);

        if (Ctx.Input.WasKeyPressed(Keys.F9))
            Console.WriteLine(_engine.Log.ToJson());
        UpdateBanner(dt);

        var soldiers = Soldiers();
        if (
            Ctx.Bindings.WasPressed(GameAction.EndTurn)
            && _engine.Validate(new EndTurnCommand()).IsValid
        )
            Play(_engine.EndPlayerPhase());
        else if (_exhaustedTimer >= 0f && (_exhaustedTimer -= dt) < 0f)
            Play(_engine.EndPlayerPhase());
        if (soldiers.Count > 0 && !IsMoving)
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
            else if (
                tile is { } dest
                && _hover is null
                && Selected is { } mover
                && Range()?.Zones.ApCostTo(dest) is not null
                && !Ctx.Ui.PointerOverUi
                && Ctx.Clicks.ConsumeClick()
            )
            {
                // Outside the range nothing happens; inside it the engine has the final say.
                var move = new MoveCommand(mover.Id, dest);
                if (_engine.Validate(move) is { IsValid: false } bad)
                    Ctx.Ui.Toast(bad.Reason);
                else
                    Play(_engine.Execute(move));
            }
        }

        SyncShields();
        foreach (var (id, view) in _views)
        {
            var unit = _state.Unit(id);
            if (!unit.IsAlive)
            {
                view.Destroy();
                _views.Remove(id);
                continue;
            }
            view.VisualCenter =
                _moving is { } m && m.Unit == id ? m.Tween.Position(m.Elapsed, out _) : null;
            view.Sync(
                selected: id == _selected,
                showOverlay: ShowOverlay(unit),
                time: _time,
                cover: id == _selected ? StandingCover(unit) : null
            );
        }
    }

    // Shields on the hovered destination; red when a visible enemy would flank it.
    private void SyncShields()
    {
        var dest =
            !IsMoving && _hover is null && Range() is { } range
                ? range.PreviewTo(_hoverTile) is { } p
                    ? p.Path[^1]
                    : (GridPos?)null
                : null;
        _shields.Sync(
            _state.Map,
            dest,
            dest is { } d && CoverCalculator.IsFlankedByAny(_state, d, Faction.Firewall, IsVisible)
        );
    }

    // Cover against the most threatening visible enemy; null when none is in view.
    private CoverResult? StandingCover(TacticalUnit unit) =>
        unit.Faction == Faction.Firewall && unit.IsAlive
            ? CoverCalculator.MostThreatening(_state, unit.Position, unit.Faction, IsVisible)
            : null;

    // Until fog of war (#21) every living enemy counts as visible.
    private static bool IsVisible(TacticalUnit unit) => true;

    private MoveCommand? MoveAttempt(GridPos dest) =>
        Selected is { } unit ? new MoveCommand(unit.Id, dest) : null;

    /// <summary>A move is playing back; input is locked until it ends.</summary>
    private bool IsMoving => _moving is not null || _moves.Count > 0;

    private void InvalidateRange() => _range = null;

    // Computed on demand and kept until the selection, the state or the map changes.
    private MoveRangeOverlay? Range()
    {
        if (_range is not null)
            return _range;
        if (
            Selected is not { IsAlive: true } unit
            || unit.Faction != Faction.Firewall
            || _state.Phase != TacticalPhase.Player
            || _state.Result is not null
            || unit.Statuses.Stunned
        )
            return null;
        return _range = new MoveRangeOverlay(Pathfinder.MoveRange(_state, unit));
    }

    private void UpdateMoveAnimation(float dt)
    {
        if (_moving is null && _moves.Count > 0)
        {
            var (unit, tween) = _moves.Dequeue();
            _moving = (unit, tween, 0f);
        }
        if (_moving is not { } m)
            return;
        var elapsed = m.Elapsed + dt;
        if (_views.TryGetValue(m.Unit, out var view))
        {
            var pos = m.Tween.Position(elapsed, out var dir);
            view.FaceToward(dir);
            // Gentle follow: re-aim a short ease at the walker every frame.
            if (m.Unit == _selected)
                _controller.FocusOn(pos, 0.2f);
        }
        _moving = m.Tween.IsDone(elapsed) ? null : m with { Elapsed = elapsed };
        if (_moving is null)
            InvalidateRange();
    }

    // Temporary: events are applied straight away (views read the state, so units jump). The
    // event player (#28) animates from a view model instead and must not read the state.
    private void Play(IReadOnlyList<GameEvent> events)
    {
        InvalidateRange();
        foreach (var e in events)
        {
            if (e is PhaseStarted p)
                _banners.Enqueue(p);
            if (e is UnitMoved moved)
                _moves.Enqueue((moved.Unit, new MoveTween(moved.Path)));
        }
        // "All soldiers exhausted": end the turn after a short beat.
        _exhaustedTimer = events.Any(e => e is AllSoldiersExhausted) ? 0.8f : -1f;
        if (_selected is { } id && !_state.Unit(id).IsAlive && Soldiers() is [var first, ..])
            Select(first);
    }

    private void UpdateBanner(float dt)
    {
        if (_banners.Count == 0)
            return;
        _bannerTime += dt;
        if (_bannerTime >= BannerSeconds)
        {
            _bannerTime = 0f;
            _banners.Dequeue();
        }
    }

    private const float BannerSeconds = 0.9f;

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

    protected override void DrawWorld(WorldOverlay overlay)
    {
        if (IsMoving || Range() is not { } range)
            return;
        range.Draw(overlay);
        if (_hover is null && range.PreviewTo(_hoverTile) is { } preview)
            MoveRangeOverlay.DrawPreview(overlay, preview.Path, preview.Ap);
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

        canvas.Text(
            $"Turn {_state.TurnNumber}   {_state.Phase} phase   Backspace: end turn   F9: copy command log",
            new Vector2(16, 92),
            TextStyles.Small
        );
        DrawSoldierAp(canvas);

        var endTurn = _engine.Validate(new EndTurnCommand());
        var buttonRect = new UiRect(
            UiSpace.LogicalWidth - 196,
            UiSpace.LogicalHeight - 64,
            180,
            48
        );
        if (
            ui.Button(
                buttonRect,
                "End Turn",
                ButtonStyle.Primary,
                enabled: endTurn.IsValid,
                tooltip: endTurn.IsValid ? null : endTurn.Reason
            )
        )
            Play(_engine.EndPlayerPhase());

        if (_hover is { } unit)
            DrawTooltip(canvas, ui, unit);
        else if (!ui.PointerOverUi && !IsMoving)
        {
            if (!DrawPreviewLabel(canvas, ui))
                DrawMoveReason(canvas, ui);
        }

        DrawBanner(canvas, ui);
    }

    private void DrawSoldierAp(ScreenCanvas canvas)
    {
        var y = 116f;
        foreach (var s in _state.Units.Where(u => u.Faction == Faction.Firewall && u.IsAlive))
        {
            canvas.Text(
                $"{s.DisplayName}: {s.ActionPoints} AP",
                new Vector2(16, y),
                TextStyles.Small
            );
            y += 18f;
        }
    }

    // "1 AP" / "2 AP (Dash)" above the hovered destination; false if there is no preview.
    private bool DrawPreviewLabel(ScreenCanvas canvas, UiContext ui)
    {
        if (Range() is not { } range || range.PreviewTo(_hoverTile) is not { } preview)
            return false;
        var dest = preview.Path[^1];
        var text = MoveRangeOverlay.Label(preview.Ap);
        var size = Ctx.Render.CanvasSize;
        var ndc = Vector2.Transform(
            TacticalCamera.TileCenter(dest.X, dest.Y) + new Vector2(0f, 0.75f),
            _camera.ToCamera2D().ViewProjection(size.X / MathF.Max(size.Y, 1f))
        );
        var at = ui.Space.FromCanvasPixels(Picking.NdcToCanvas(ndc, size));
        var width = canvas.Measure(text, TextStyles.Tooltip);
        canvas.Text(text, at - new Vector2(width / 2f, 8f), TextStyles.Tooltip);
        return true;
    }

    // Why a move to the hovered tile is not allowed, next to the cursor.
    private void DrawMoveReason(ScreenCanvas canvas, UiContext ui)
    {
        var tile = PickTile(Ctx.Render.CanvasSize);
        if (tile is not { } dest || MoveAttempt(dest) is not { } move)
            return;
        var result = _engine.Validate(move);
        if (result.IsValid)
            return;
        var width = canvas.Measure(result.Reason, TextStyles.Tooltip);
        var x = MathF.Min(ui.Mouse.X + 18f, ui.Space.CanvasRect.Right - width - 4f);
        canvas.Text(result.Reason, new Vector2(x, ui.Mouse.Y + 18f), TextStyles.Tooltip);
    }

    private void DrawBanner(ScreenCanvas canvas, UiContext ui)
    {
        if (_banners.Count == 0 && _state.Result is null)
            return;
        var text =
            _banners.Count > 0
                ? _banners.Peek() switch
                {
                    { Phase: TacticalPhase.Player } p => $"PLAYER PHASE  -  TURN {p.Turn}",
                    { Phase: TacticalPhase.Enemy } => "ENEMY PHASE",
                    _ => "ENVIRONMENT PHASE",
                }
                : $"MISSION {_state.Result!.Value.ToString().ToUpperInvariant()}";
        var rect = ui.Space.CanvasRect;
        var band = new Vector2(rect.W, 64f);
        var pos = new Vector2(rect.X, rect.Y + rect.H / 2f - 32f);
        canvas.FillRect(pos, band, new Vector4(0.02f, 0.03f, 0.05f, 0.8f));
        canvas.Text(
            text,
            new Vector2(
                UiSpace.LogicalWidth / 2f - canvas.Measure(text, TextStyles.Heading) / 2f,
                pos.Y + 18f
            ),
            TextStyles.Heading
        );
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
