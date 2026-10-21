using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Data;
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
/// "Skirmish (debug)": the test yard with a debug squad of four and three enemy pods, played through
/// the tactical HUD (#27). Click a soldier, the strip or Tab (soldiers with AP) to select, F
/// re-centres, 1 to 9 use the ability bar, Backspace ends the turn, Esc cancels aiming or opens the
/// pause menu. The mission ends on a Purge or a wipe (or Abort Mission) and moves on to the result
/// screen.
/// </summary>
public sealed class SkirmishScene : SceneBase
{
    public const string MapId = "test-yard";

    private static readonly IReadOnlyList<PodSpec> Pods =
    [
        new("Husk Swarm", ["husk", "husk", "husk"]),
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
    private FogView _fog = null!;
    private bool _fogEnabled = true;
    private UnitId? _selected;
    private MoveRangeOverlay? _range;
    private GridPos? _hoverTile;
    private readonly Queue<Playback> _moves = new();
    private (UnitId Unit, MoveTween Tween, float Elapsed)? _moving;
    private (string Name, float Elapsed)? _reveal;
    private bool _cameraReturn;
    private TacticalUnit? _hover;
    private TargetingMode? _targeting;
    private TargetReticleView _reticle = null!;
    private float _time;
    private readonly Dictionary<UnitId, float> _reloading = [];
    private readonly List<FloatingText> _floaters = [];

    private bool _confirmAbort;
    private bool _confirmEnd;
    private bool _autoSelect;
    private float _resultTimer = -1f;

    private const float ReloadPoseSeconds = 0.6f;
    private const float RevealSeconds = 1.2f;
    private const float FloaterSeconds = 1.4f;
    private const float ResultDelaySeconds = 2f;

    // One beat of the playback: a unit walking, or the pod reveal (banner and camera) between walks.
    private sealed record Playback(UnitId Unit, MoveTween? Tween, string? PodName, GridPos At);

    // Temporary world-anchored text until the event player (#28).
    private sealed record FloatingText(string Text, GridPos Tile)
    {
        public float Age { get; set; }
    }

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
        _engine = new TacticalEngine(
            _state,
            Ctx.Data,
            new UtilityAi(Ctx.Data, timingLog: LogAiTiming)
        );

        map.TileChanged += _ => InvalidateRange();
        _tilemaps = MapTilemaps.Build(World, map, Ctx.Assets);
        _shields = new CoverShieldsView(World, Ctx.Assets);
        _fog = new FogView(World, map.Width, map.Height);
        _reticle = new TargetReticleView(World, Ctx.Assets);
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

    // The soldiers who can still be selected: on the map and not down.
    private List<TacticalUnit> Soldiers() =>
        _state.Units.Where(u => u.Faction == Faction.Firewall && u.IsStanding).ToList();

    // Debug builds log how long each enemy took to plan (budget: 30 ms per unit in the browser).
    private static void LogAiTiming(AiTiming timing)
    {
#if DEBUG
        Console.WriteLine(
            $"[AI] {timing.Unit} ({timing.Profile}): {timing.Milliseconds:F1} ms over {timing.Steps} steps"
        );
#endif
    }

    private void Select(TacticalUnit unit, bool instant = false)
    {
        _selected = unit.Id;
        _targeting = null;
        InvalidateRange();
        _controller.FocusOn(UnitFocus(unit), instant ? 0f : 0.35f);
    }

    protected override void OnUpdate(float dt)
    {
        _time += dt;
        if (_resultTimer >= 0f && (_resultTimer -= dt) < 0f && _state.Result is { } result)
        {
            Ctx.Scenes.Replace(new MissionResultScene(result));
            return;
        }
        // The Abort Mission dialog takes the input (its own Esc cancels it).
        if (_confirmAbort || _confirmEnd)
        {
            UpdateFloaters(dt);
            SyncViews();
            return;
        }
        if (Ctx.Bindings.WasPressed(GameAction.Cancel))
        {
            if (_targeting is not null)
                ExitTargeting();
            else
            {
                Ctx.Scenes.Push(new PauseOverlayScene());
                return;
            }
        }

        var canvas = Ctx.Render.CanvasSize;
        _controller.Update(dt, canvas, Ctx.Ui.PointerOverUi);
        if (_controller.RightClicked && _targeting is not null)
            ExitTargeting();
        var tile = Ctx.Ui.PointerOverUi ? null : PickTile(canvas);
        _hoverTile = tile;
        _hover =
            tile is { } t && _state.UnitAt(t) is { } hovered && IsVisible(hovered) ? hovered : null;
        UpdateMoveAnimation(dt);

        if (Ctx.Input.WasKeyPressed(Keys.F2))
            _fogEnabled = !_fogEnabled;
        if (Ctx.Input.WasKeyPressed(Keys.F8))
            DebugTeleportEnemy(tile);
        if (Ctx.Input.WasKeyPressed(Keys.F9))
            Console.WriteLine(_engine.Log.ToJson());
        UpdateBanner(dt);
        UpdateFloaters(dt);

        var soldiers = Soldiers();
        if (Ctx.Bindings.WasPressed(GameAction.EndTurn))
            RequestEndTurn();
        else if (_exhaustedTimer >= 0f && (_exhaustedTimer -= dt) < 0f)
            Play(_engine.EndPlayerPhase());
        if (_autoSelect && !IsMoving)
            AutoSelect();
        if (_targeting is not null && !IsMoving)
            UpdateTargeting();
        else if (soldiers.Count > 0 && !IsMoving)
        {
            if (
                HudSlots()
                    .FirstOrDefault(sl => sl.Key is { } key && Ctx.Bindings.WasPressed(key)) is
                { } slot
            )
            {
                if (SlotBlocked(slot) is { } why)
                    Ctx.Ui.Toast(why);
                else
                    ActivateAbility(slot.AbilityId);
            }
            else if (Ctx.Bindings.WasPressed(GameAction.NextUnit))
            {
                if (
                    HudModel.NextWithAp(
                        soldiers,
                        _selected,
                        Ctx.Input.IsKeyPressed(Keys.Shift) ? -1 : 1
                    ) is
                    { } next
                )
                    Select(next);
            }
            else if (Ctx.Bindings.WasPressed(GameAction.CenterCamera) && Selected is { } sel)
                Select(sel);
            else if (
                _hover is { Faction: Faction.Firewall, IsStanding: true } clicked
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
        _reticle.Sync(_targeting?.Current.Unit.Position, _time);
        _fog.Sync(_fogEnabled ? _state.PlayerVision : null);
        SyncViews();
    }

    private void SyncViews()
    {
        foreach (var (id, view) in _views)
        {
            var unit = _state.Unit(id);
            // Killed or evacuated: gone from the map.
            if (!unit.IsOnMap)
            {
                view.Destroy();
                _views.Remove(id);
                continue;
            }
            // A unit still waiting its turn in the playback stays where its next walk starts.
            view.VisualCenter =
                _moving is { } m && m.Unit == id ? m.Tween.Position(m.Elapsed, out _)
                : _moves.FirstOrDefault(p => p.Tween is not null && p.Unit == id)?.Tween is { } next
                    ? next.Position(0f, out _)
                : null;
            view.Reloading = _reloading.ContainsKey(id);
            view.Sync(
                selected: id == _selected,
                showOverlay: ShowOverlay(unit),
                time: _time,
                cover: id == _selected ? StandingCover(unit) : null,
                visible: IsVisible(unit)
            );
        }
    }

    // ------------------------------------------------------------------ self abilities

    private UseAbilityCommand? SelfCommand(string abilityId) =>
        Selected is { } unit ? new UseAbilityCommand(unit.Id, abilityId, Target.None) : null;

    // Why the selected soldier cannot use the ability right now; null when it can.
    private string? SelfAbilityBlocked(string abilityId) =>
        IsMoving ? "Wait for the move to finish."
        : SelfCommand(abilityId) is not { } cmd ? "Select a soldier."
        : _engine.Validate(cmd) is { IsValid: false } bad ? bad.Reason
        : null;

    private void UseSelfAbility(string abilityId)
    {
        if (SelfAbilityBlocked(abilityId) is { } reason)
            Ctx.Ui.Toast(reason);
        else
            Play(_engine.Execute(SelfCommand(abilityId)!));
    }

    // An adjacent bleeding-out squadmate of the selected soldier, if any.
    private TacticalUnit? StabilizeTarget() =>
        Selected is not { } medic
            ? null
            : _state.Units.FirstOrDefault(u =>
                u.Faction == Faction.Firewall
                && u.IsOnMap
                && u.Statuses.IsBleedingOut
                && Math.Max(
                    Math.Abs(u.Position.X - medic.Position.X),
                    Math.Abs(u.Position.Y - medic.Position.Y)
                ) == 1
            );

    private UseAbilityCommand? StabilizeCommand() =>
        Selected is { } medic && StabilizeTarget() is { } patient
            ? new UseAbilityCommand(
                medic.Id,
                AbilityRegistry.DebugStabilize,
                Target.Unit(patient.Id)
            )
            : null;

    private void Stabilize()
    {
        if (IsMoving)
            return;
        if (StabilizeCommand() is not { } cmd)
            Ctx.Ui.Toast("No bleeding-out squadmate next to this soldier.");
        else if (_engine.Validate(cmd) is { IsValid: false } bad)
            Ctx.Ui.Toast(bad.Reason);
        else
            Play(_engine.Execute(cmd));
    }

    // Evac is offered only while the selected soldier stands on an evac tile.
    private bool OnEvacTile => Selected is { } s && _state.Map.EvacTiles.Contains(s.Position);

    // ------------------------------------------------------------------ targeting

    private TargetingMode? TryBeginTargeting(out string reason)
    {
        if (Selected is not { } shooter || IsMoving)
        {
            reason = "Select a soldier.";
            return null;
        }
        return TargetingMode.TryBegin(_engine, shooter, IsVisible, out reason);
    }

    private void BeginTargeting()
    {
        if (TryBeginTargeting(out var reason) is not { } mode)
        {
            Ctx.Ui.Toast(reason);
            return;
        }
        _targeting = mode;
        FrameTarget();
    }

    private void ExitTargeting()
    {
        _targeting = null;
        if (Selected is { } sel)
            _controller.FocusOn(UnitFocus(sel), 0.35f);
    }

    // Eases the camera to frame the shooter and the current target.
    private void FrameTarget()
    {
        if (_targeting is not { } mode)
            return;
        var a = _state.Unit(mode.Shooter).Position;
        var b = mode.Current.Unit.Position;
        _controller.FocusOn(
            HudModel.FocusPoint(
                (TacticalCamera.TileCenter(a.X, a.Y) + TacticalCamera.TileCenter(b.X, b.Y)) / 2f,
                _camera.TilesVisibleVertically
            ),
            0.35f
        );
    }

    private void UpdateTargeting()
    {
        var mode = _targeting!;
        if (Ctx.Bindings.WasPressed(GameAction.NextUnit))
        {
            mode.Cycle(Ctx.Input.IsKeyPressed(Keys.Shift) ? -1 : 1);
            FrameTarget();
        }
        else if (Ctx.Bindings.WasPressed(GameAction.Confirm))
            Fire();
        else if (
            _hover is { } clicked
            && clicked.Faction != Faction.Firewall
            && !Ctx.Ui.PointerOverUi
            && Ctx.Clicks.ConsumeClick()
        )
        {
            if (clicked.Id == mode.Current.Unit.Id)
                Fire();
            else if (mode.Select(clicked.Id))
                FrameTarget();
        }
    }

    private void Fire()
    {
        if (_targeting is not { } mode)
            return;
        var cmd = mode.Command;
        _targeting = null;
        if (_engine.Validate(cmd) is { IsValid: false } bad)
            Ctx.Ui.Toast(bad.Reason);
        else
            Play(_engine.Execute(cmd));
    }

    // Debug (F8): moves the nearest enemy to the hovered free tile, to check the flank shields.
    private void DebugTeleportEnemy(GridPos? tile)
    {
        if (tile is not { } to || !_state.Map.IsWalkable(to) || _state.IsOccupied(to))
            return;
        var enemy = _state
            .Units.Where(u => u.Faction == Faction.Meridian && u.IsAlive)
            .OrderBy(u => u.Position.DistanceTo(to))
            .FirstOrDefault();
        if (enemy is null)
            return;
        enemy.Position = to;
        _state.RefreshVision();
        InvalidateRange();
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

    // Hostiles on tiles the squad cannot see are not drawn, hovered or targeted (F2 turns fog off).
    private bool IsVisible(TacticalUnit unit) =>
        !_fogEnabled || _state.PlayerVision.IsUnitVisible(unit);

    private MoveCommand? MoveAttempt(GridPos dest) =>
        Selected is { } unit ? new MoveCommand(unit.Id, dest) : null;

    /// <summary>A move is playing back; input is locked until it ends.</summary>
    private bool IsMoving => _moving is not null || _reveal is not null || _moves.Count > 0;

    private void InvalidateRange() => _range = null;

    // Computed on demand and kept until the selection, the state or the map changes.
    private MoveRangeOverlay? Range()
    {
        if (_targeting is not null)
            return null;
        if (_range is not null)
            return _range;
        if (
            Selected is not { IsAlive: true } unit
            || unit.Faction != Faction.Firewall
            || _state.Phase != TacticalPhase.Player
            || _state.Result is not null
            || unit.Statuses.Stunned
            || !unit.IsStanding
        )
            return null;
        return _range = new MoveRangeOverlay(_engine.MoveRange(unit));
    }

    private void UpdateMoveAnimation(float dt)
    {
        if (_reveal is { } reveal)
        {
            // The pod reveal holds everything else: the banner shows, then the scampers play.
            _reveal =
                reveal.Elapsed + dt >= RevealSeconds
                    ? null
                    : reveal with
                    {
                        Elapsed = reveal.Elapsed + dt,
                    };
            return;
        }
        if (_moving is null && _moves.Count > 0)
        {
            var next = _moves.Dequeue();
            if (next.Tween is null)
            {
                _reveal = (next.PodName ?? "", 0f);
                _cameraReturn = true;
                _controller.FocusOn(TacticalCamera.TileCenter(next.At.X, next.At.Y), 0.35f);
                return;
            }
            _moving = (next.Unit, next.Tween, 0f);
        }
        if (_moving is null && _cameraReturn && _moves.Count == 0)
        {
            _cameraReturn = false;
            if (Selected is { } sel)
                _controller.FocusOn(UnitFocus(sel), 0.35f);
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
                _controller.FocusOn(HudModel.FocusPoint(pos, _camera.TilesVisibleVertically), 0.2f);
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
        _targeting = null;
        foreach (var e in events)
        {
            if (e is UnitFacing facing && _views.TryGetValue(facing.Unit, out var turning))
            {
                var from = _state.Unit(facing.Unit).Position;
                turning.FaceToward(new Vector2(facing.LookAt.X - from.X, facing.LookAt.Y - from.Y));
            }
            if (e is ShotResolved shot)
                Ctx.Ui.Toast(ShotText(shot));
            if (e is ReactionShotTriggered reaction)
                Float("REACTION FIRE", reaction.Shooter);
            if (e is StatusAdded { Status: StatusFlags.Overwatch } overwatch)
                Float("OVERWATCH", overwatch.Unit);
            if (e is StatusAdded { Status: StatusFlags.Hunkered } hunker)
                Float("HUNKERED", hunker.Unit);
            if (e is AbilityUsed { AbilityId: AbilityRegistry.Reload } reload)
                _reloading[reload.Unit] = ReloadPoseSeconds;
            if (e is PhaseStarted p)
                _banners.Enqueue(p);
            if (e is MoveBlocked)
                Ctx.Ui.Toast("Something is in the way.");
            if (e is UnitBleedingOut { Turns: Casualties.BleedOutTurns } down)
                OnBleedingOut(down.Unit);
            if (e is UnitStabilized stabilized)
                Float("STABILIZED", stabilized.Unit);
            if (e is UnitEvacuated evacuated)
                Ctx.Ui.Toast($"{_state.Unit(evacuated.Unit).DisplayName} evacuated.");
            if (e is MissionEnded)
                _resultTimer = ResultDelaySeconds;
            if (e is UnitMoved moved && ShouldAnimate(moved.Unit, moved.Path))
                _moves.Enqueue(new Playback(moved.Unit, new MoveTween(moved.Path), null, default));
            if (e is UnitScampered scamper && ShouldAnimate(scamper.Unit, scamper.Path))
                _moves.Enqueue(
                    new Playback(scamper.Unit, new MoveTween(scamper.Path), null, default)
                );
            if (e is PodActivated pod)
                _moves.Enqueue(new Playback(default, null, pod.Name.ToUpperInvariant(), pod.At));
        }
        // "All soldiers exhausted": end the turn after a short beat.
        _exhaustedTimer = events.Any(e => e is AllSoldiersExhausted) ? 0.8f : -1f;
        _autoSelect = true;
    }

    // After an action: stay on a soldier with AP left, else the next one with AP.
    private void AutoSelect()
    {
        _autoSelect = false;
        if (_state.Phase != TacticalPhase.Player || _state.Result is not null)
            return;
        var soldiers = Soldiers();
        if (HudModel.SelectionAfterAction(soldiers, _selected) is { } next && next.Id != _selected)
            Select(next);
    }

    // Soldiers always walk on screen; an enemy only if the squad can see where it starts or ends,
    // so patrols in the fog play instantly and show nothing.
    private bool ShouldAnimate(UnitId unit, IReadOnlyList<GridPos> path) =>
        _state.Unit(unit).Faction != Faction.Meridian
        || _state.PlayerVision.IsVisible(path[0])
        || _state.PlayerVision.IsVisible(path[^1]);

    // Someone just went down: say so and look at them.
    private void OnBleedingOut(UnitId id)
    {
        var unit = _state.Unit(id);
        Ctx.Ui.Toast(
            $"{unit.DisplayName} is bleeding out! {Casualties.BleedOutTurns} turns to stabilize.",
            4f
        );
        _controller.FocusOn(UnitFocus(unit), 0.35f);
    }

    // Temporary feedback until the event player (#28) shows floating numbers.
    private string ShotText(ShotResolved shot)
    {
        var unit = _state.Unit(shot.Target);
        var target = unit.DisplayName;
        if (!shot.Hit)
            return $"Missed {target} ({shot.HitChance}%).";
        // Nobody shoots a downed unit, so one that is down now went down to this shot.
        var kill =
            !unit.IsAlive ? " Killed!"
            : unit.Statuses.IsDown ? " Down!"
            : "";
        return shot.Crit
            ? $"Critical! {shot.Damage} damage to {target}.{kill}"
            : $"Hit {target} for {shot.Damage}.{kill}";
    }

    private void Float(string text, UnitId unit)
    {
        var u = _state.Unit(unit);
        if (IsVisible(u))
            _floaters.Add(new FloatingText(text, u.Position));
    }

    private void UpdateFloaters(float dt)
    {
        foreach (var f in _floaters)
            f.Age += dt;
        _floaters.RemoveAll(f => f.Age >= FloaterSeconds);
        foreach (var id in _reloading.Keys.ToList())
            if ((_reloading[id] -= dt) <= 0f)
                _reloading.Remove(id);
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

    // Soldiers always; enemies while visible.
    private bool ShowOverlay(TacticalUnit unit) => unit.IsOnMap && IsVisible(unit);

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
        DrawEvacZone(overlay);
        if (_targeting is { } mode)
        {
            DrawAimLine(overlay, mode);
            return;
        }
        if (IsMoving || Range() is not { } range)
            return;
        range.Draw(overlay);
        if (_hover is null && range.PreviewTo(_hoverTile) is { } preview)
            MoveRangeOverlay.DrawPreview(overlay, preview.Path, preview.Ap);
    }

    // Evac tiles: a green wash, and a pulsing flare in the middle of each.
    private void DrawEvacZone(WorldOverlay overlay)
    {
        var pulse = 0.5f + 0.5f * MathF.Sin(_time * 2f * MathF.PI / 1.2f);
        foreach (var tile in _state.Map.EvacTiles)
        {
            overlay.FillTile(tile.X, tile.Y, new Vector4(0.25f, 0.9f, 0.4f, 0.16f));
            var size = 0.12f + 0.08f * pulse;
            var center = TacticalCamera.TileCenter(tile.X, tile.Y);
            overlay.FillRect(
                center - new Vector2(size / 2f),
                new Vector2(size),
                new Vector4(0.45f, 1f, 0.5f, 0.55f + 0.45f * pulse)
            );
        }
    }

    // A dashed line from the shooter to the target.
    private void DrawAimLine(WorldOverlay overlay, TargetingMode mode)
    {
        var a = _state.Unit(mode.Shooter).Position;
        var b = mode.Current.Unit.Position;
        var from = TacticalCamera.TileCenter(a.X, a.Y);
        var to = TacticalCamera.TileCenter(b.X, b.Y);
        var length = Vector2.Distance(from, to);
        if (length < 0.01f)
            return;
        var dir = (to - from) / length;
        const float dash = 0.3f;
        const float gap = 0.2f;
        var color = new Vector4(1f, 0.35f, 0.27f, 0.85f);
        // Start and stop clear of the unit sprites.
        for (var t = 0.4f; t < length - 0.5f; t += dash + gap)
            overlay.Line(
                from + dir * t,
                from + dir * MathF.Min(t + dash, length - 0.5f),
                0.05f,
                color
            );
    }

    protected override void DrawScreen(ScreenCanvas canvas, UiContext ui)
    {
        var hud = TacticalHud.Draw(canvas, ui, BuildHud());
        ApplyHud(hud);

        if (_targeting is { } mode)
            ShotPanel.Draw(
                canvas,
                ui,
                mode.Current,
                _state.Unit(mode.Shooter).Weapon,
                mode.Candidates.Count,
                TacticalHud.AboveBar
            );

        if (_hover is { } unit)
            DrawTooltip(canvas, ui, unit);
        else if (_targeting is null && !ui.PointerOverUi && !IsMoving)
        {
            if (!DrawPreviewLabel(canvas, ui))
                DrawMoveReason(canvas, ui);
        }

        DrawBleedOutCounters(canvas, ui);
        DrawFloaters(canvas, ui);
        DrawPodBanner(canvas, ui);
        if (_confirmAbort)
            DrawAbortDialog(ui);
        else if (_confirmEnd)
            DrawEndTurnDialog(ui);
    }

    // ------------------------------------------------------------------ HUD glue

    /// <summary>Playback or the enemy phase is running (or the mission is over): input is locked.</summary>
    private bool Busy =>
        IsMoving || _state.Phase != TacticalPhase.Player || _state.Result is not null;

    private IReadOnlyList<AbilitySlot> HudSlots() =>
        Selected is { } unit
            ? HudModel.AbilitySlots(unit, Ctx.Data, OnEvacTile, StabilizeCommand() is not null)
            : [];

    // Why the slot cannot be used right now; null when it can.
    private string? SlotBlocked(AbilitySlot slot)
    {
        if (slot.AbilityId == AbilityRegistry.Shoot)
            return _targeting is not null || TryBeginTargeting(out var reason) is not null
                ? null
                : reason;
        if (slot.AbilityId == AbilityRegistry.DebugStabilize)
            return StabilizeCommand() is not { } cmd
                    ? "No bleeding-out squadmate next to this soldier."
                : IsMoving ? "Wait for the move to finish."
                : _engine.Validate(cmd) is { IsValid: false } bad ? bad.Reason
                : null;
        return Ctx.Data.Abilities[slot.AbilityId].Targeting == TargetingType.Self
            ? SelfAbilityBlocked(slot.AbilityId)
            : "Not available yet.";
    }

    private HudFrame BuildHud()
    {
        var animated = _banners.Count > 0;
        return new HudFrame
        {
            Squad = _state.Units.Where(u => u.Faction == Faction.Firewall).ToList(),
            Selected = Selected,
            Abilities = HudSlots().Select(s => new HudAbility(s, SlotBlocked(s))).ToList(),
            ArmedAbility = _targeting?.AbilityId,
            Enemies = VisibleEnemies(),
            Objective = HudModel.ObjectiveText(_state, Ctx.Data, _engine.Objective, IsVisible),
            MissionName = "Skirmish: Test Yard",
            EndTurnBlocked = _engine.Validate(new EndTurnCommand()) is { IsValid: false } end
                ? end.Reason
                : null,
            AbortBlocked =
                _engine.Validate(new AbortMissionCommand()) is { IsValid: false } abort
                    ? abort.Reason
                : _targeting is not null ? "Cancel targeting first."
                : null,
            Busy = Busy,
            BannerText =
                animated ? HudModel.PhaseText(_banners.Peek().Phase, _banners.Peek().Turn)
                : _state.Result is { } result ? MissionResultText.Title(result.Outcome)
                : null,
            BannerTime = _bannerTime,
            BannerSeconds = BannerSeconds,
            BannerStatic = !animated,
            Time = _time,
        };
    }

    private List<HudEnemy> VisibleEnemies() =>
        _state
            .Units.Where(u =>
                u is { Faction: Faction.Meridian, IsOnMap: true }
                && !u.Statuses.IsDown
                && Ctx.Data.Enemies[u.DefId].Kind == EnemyKind.Unit
                && IsVisible(u)
            )
            .Select(u => new HudEnemy(
                u,
                Ctx.Data.Enemies[u.DefId].Name,
                _targeting?.Candidates.FirstOrDefault(c => c.Unit.Id == u.Id)?.Preview.HitChance,
                _targeting?.Current.Unit.Id == u.Id
            ))
            .ToList();

    private void ApplyHud(HudResult hud)
    {
        if (hud.Settings)
            Ctx.Scenes.Push(new PauseOverlayScene());
        else if (hud.EndTurn)
            RequestEndTurn();
        else if (hud.Abort)
            _confirmAbort = true;
        else if (hud.Ability is { } abilityId)
            ActivateAbility(abilityId);
        else if (hud.ClickedEnemy is { } enemy)
            OnEnemyClicked(enemy);
        else if (
            hud.SelectSoldier is { } id
            && !IsMoving
            && _state.Unit(id) is { IsStanding: true } s
        )
            Select(s);
        else if (hud.CenterSelected && Selected is { } selected)
            Select(selected);
    }

    // Uses the ability in the given bar slot (a hotkey or a click); the reason shows as a toast.
    private void ActivateAbility(string abilityId)
    {
        if (Busy)
            return;
        if (abilityId == AbilityRegistry.Shoot)
        {
            if (_targeting is not null)
                ExitTargeting();
            else
                BeginTargeting();
        }
        else if (abilityId == AbilityRegistry.DebugStabilize)
            Stabilize();
        else if (Ctx.Data.Abilities[abilityId].Targeting == TargetingType.Self)
            UseSelfAbility(abilityId);
        else
            Ctx.Ui.Toast("Not available yet.");
    }

    // Targets the enemy while Shoot is armed, otherwise looks at it.
    private void OnEnemyClicked(UnitId id)
    {
        if (_targeting is { } mode)
        {
            if (mode.Select(id))
                FrameTarget();
            else
                Ctx.Ui.Toast("That enemy cannot be targeted.");
            return;
        }
        var enemy = _state.Unit(id);
        _controller.FocusOn(UnitFocus(enemy), 0.35f);
    }

    // Soldiers with AP left make the player confirm; otherwise the turn just ends.
    private void RequestEndTurn()
    {
        if (Busy || !_engine.Validate(new EndTurnCommand()).IsValid)
            return;
        if (Soldiers().Any(u => u.ActionPoints > 0))
            _confirmEnd = true;
        else
            Play(_engine.EndPlayerPhase());
    }

    private void DrawEndTurnDialog(UiContext ui)
    {
        var idle = Soldiers().Where(u => u.ActionPoints > 0).Select(u => u.DisplayName).ToList();
        var body = $"Still have action points: {string.Join(", ", idle)}.";
        var choice = ui.Modal("End turn?", body, ["End Turn", "Cancel"], 0, 1);
        if (choice == UiContext.ModalPending)
            return;
        _confirmEnd = false;
        if (choice == 0 && !Busy && _engine.Validate(new EndTurnCommand()).IsValid)
            Play(_engine.EndPlayerPhase());
    }

    // The point the camera aims at to show a unit above the bottom panels.
    private Vector2 UnitFocus(TacticalUnit unit) =>
        HudModel.FocusPoint(
            TacticalCamera.TileCenter(unit.Position.X, unit.Position.Y),
            _camera.TilesVisibleVertically
        );

    private void DrawAbortDialog(UiContext ui)
    {
        var onMap = _state.Units.Where(u => u.Faction == Faction.Firewall && u.IsOnMap).ToList();
        var leaving = onMap
            .Where(u => u.IsStanding && _state.Map.EvacTiles.Contains(u.Position))
            .Select(u => u.DisplayName)
            .ToList();
        var left = onMap.Select(u => u.DisplayName).Except(leaving).ToList();
        var body =
            $"Soldiers in the evac zone leave: {(leaving.Count == 0 ? "nobody" : string.Join(", ", leaving))}.\n"
            + $"Left behind (MIA): {(left.Count == 0 ? "nobody" : string.Join(", ", left))}.\n"
            + "No loot is recovered.";
        var choice = ui.Modal("Abort Mission?", body, ["Abort", "Cancel"], 1, 1);
        if (choice == UiContext.ModalPending)
            return;
        _confirmAbort = false;
        if (choice == 0 && _engine.Validate(new AbortMissionCommand()).IsValid)
            Play(_engine.Execute(new AbortMissionCommand()));
    }

    // Turns left above each visible bleeding-out soldier, in red.
    private void DrawBleedOutCounters(ScreenCanvas canvas, UiContext ui)
    {
        var size = Ctx.Render.CanvasSize;
        var vp = _camera.ToCamera2D().ViewProjection(size.X / MathF.Max(size.Y, 1f));
        var style = new TextStyle(TextStyles.Future, 18, new Color(255, 80, 70), Shadow: true);
        foreach (var (id, view) in _views)
        {
            var unit = _state.Unit(id);
            if (!unit.Statuses.IsBleedingOut || !IsVisible(unit))
                continue;
            var anchor = new Vector2(unit.Position.X + 0.5f, view.IconRowBottom + 0.45f);
            var at = ui.Space.FromCanvasPixels(
                Picking.NdcToCanvas(Vector2.Transform(anchor, vp), size)
            );
            var text = unit.Statuses.BleedingOutTurnsLeft.ToString();
            canvas.Text(text, at - new Vector2(canvas.Measure(text, style) / 2f, 10f), style);
        }
    }

    // "OVERWATCH" / "REACTION FIRE" rising above the unit's tile and fading out.
    private void DrawFloaters(ScreenCanvas canvas, UiContext ui)
    {
        var size = Ctx.Render.CanvasSize;
        var vp = _camera.ToCamera2D().ViewProjection(size.X / MathF.Max(size.Y, 1f));
        foreach (var f in _floaters)
        {
            var t = f.Age / FloaterSeconds;
            var ndc = Vector2.Transform(
                TacticalCamera.TileCenter(f.Tile.X, f.Tile.Y) + new Vector2(0f, 0.9f + 0.5f * t),
                vp
            );
            var at = ui.Space.FromCanvasPixels(Picking.NdcToCanvas(ndc, size));
            var alpha = (byte)(255 * Math.Clamp(1.5f - 1.5f * t, 0f, 1f));
            var style = new TextStyle(
                TextStyles.Future,
                20,
                new Color(255, 214, 92, alpha),
                Shadow: true
            );
            var width = canvas.Measure(f.Text, style);
            canvas.Text(f.Text, at - new Vector2(width / 2f, 10f), style);
        }
    }

    // "1 AP" / "2 AP (Dash)" above the hovered destination; false if there is no preview.
    private bool DrawPreviewLabel(ScreenCanvas canvas, UiContext ui)
    {
        if (Range() is not { } range || range.PreviewTo(_hoverTile) is not { } preview)
            return false;
        var dest = preview.Path[^1];
        var text = MoveRangeOverlay.Label(preview.Ap);
        var northCover =
            CoverCalculator.GetProfile(_state.Map, dest).North != CoverLevel.None ? 0.3f : 0f;
        var size = Ctx.Render.CanvasSize;
        var ndc = Vector2.Transform(
            TacticalCamera.TileCenter(dest.X, dest.Y) + new Vector2(0f, 0.75f + northCover),
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

    // Top centre, for the length of the pod reveal beat.
    private void DrawPodBanner(ScreenCanvas canvas, UiContext ui)
    {
        if (_reveal is not { } reveal)
            return;
        var width = canvas.Measure(reveal.Name, TextStyles.Heading) + 64f;
        var pos = new Vector2(UiSpace.LogicalWidth / 2f - width / 2f, ui.Space.CanvasRect.Y + 140f);
        canvas.FillRect(pos, new Vector2(width, 48f), new Vector4(0.45f, 0.06f, 0.06f, 0.85f));
        canvas.Text(reveal.Name, new Vector2(pos.X + 32f, pos.Y + 10f), TextStyles.Heading);
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
