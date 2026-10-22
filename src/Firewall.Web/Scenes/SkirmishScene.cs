using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Data;
using Firewall.Rules.Rng;
using Firewall.Rules.Tactical;
using Firewall.Web.Input;
using Firewall.Web.Playback;
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
/// pause menu. The squad is four rookies unless the squad builder (#30) passes soldiers. The mission ends on a Purge or a wipe (or Abort Mission) and moves on to the result
/// screen. The pods come from the encounter table of the chosen Force Level (#29). Debug keys: F6
/// picks an enemy type, F7 its tier, F10 spawns it on the hovered tile (a Relay Node or a pod member).
/// </summary>
public sealed class SkirmishScene(int forceLevel = 2, IReadOnlyList<SoldierSpec>? squad = null)
    : SceneBase
{
    public const string MapId = "test-yard";

    // The test yard has three enemy spawns.
    private const int PodCount = 3;

    private readonly string[] _debugEnemies =
    [
        "husk",
        "warden",
        "proxy",
        "enforcer",
        "relay_node",
        "cradle_guardian",
    ];
    private int _debugEnemy;
    private int _debugTier;

    private readonly TacticalCamera _camera = new();
    private readonly Dictionary<UnitId, UnitView> _views = [];
    private TacticalCameraController _controller = null!;
    private TacticalState _state = null!;
    private TacticalEngine _engine = null!;
    private float _exhaustedTimer = -1f;
    private MapTilemaps? _tilemaps;
    private CoverShieldsView _shields = null!;
    private FogView _fog = null!;
    private bool _fogEnabled = true;
    private UnitId? _selected;
    private MoveRangeOverlay? _range;
    private GridPos? _hoverTile;
    private ViewModel _vm = null!;
    private EventPlayer _player = null!;
    private CameraDirector _director = null!;
    private FloatersView _floatersView = null!;
    private EffectsView _effects = null!;
    private TacticalUnit? _hover;
    private TargetingMode? _targeting;
    private TargetReticleView _reticle = null!;
    private float _time;

    private bool _confirmAbort;
    private bool _confirmEnd;
    private bool _autoSelect;
    private float _resultTimer = -1f;

    private const float ResultDelaySeconds = 2f;

    public override string Name => squad is null ? $"Skirmish FL{forceLevel}" : "Skirmish (squad)";

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
        SquadDeployer.Deploy(
            _state,
            squad is null
                ? DebugSquad.Create(_state, Ctx.Data, rng.Fork("squad"))
                : DebugSquad.Create(_state, Ctx.Data, rng.Fork("squad"), squad)
        );
        var pods = EncounterBuilder.Build(Ctx.Data, forceLevel, PodCount, rng.Fork("encounter"));
        PodSpawner.Spawn(_state, Ctx.Data, pods, rng.Fork("pods"));
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
        _vm = ViewModel.FromState(_state, Ctx.Data);
        foreach (var unit in _vm.Units.Values)
            _views[unit.Id] = new UnitView(World, Ctx.Assets, Ctx.Data, unit);
        _floatersView = new FloatersView(World);
        _effects = new EffectsView(World, Ctx.Assets);

        _controller = new TacticalCameraController(_camera, Ctx.Input, Ctx.Bindings)
        {
            MapSize = new Vector2(map.Width, map.Height),
        };
        _camera.Center = new Vector2(map.Width / 2f, map.Height / 2f);
        _director = new CameraDirector(_camera, _controller, () => Ctx.Render.CanvasSize);
        _player = new EventPlayer(
            _vm,
            new EventPlayerContext
            {
                Camera = _director,
                IsTileVisible = tile => !_fogEnabled || _state.PlayerVision.IsVisible(tile),
                SceneEvent = OnSceneEvent,
            },
            AnimationFactory.Create
        );
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
        _director.Cancel();
        _controller.FocusOn(UnitFocus(unit), instant ? 0f : 0.35f);
    }

    protected override void OnUpdate(float dt)
    {
        _time += dt;
        UpdatePlayback(dt);
        if (_resultTimer >= 0f && (_resultTimer -= dt) < 0f && _state.Result is { } result)
        {
            Ctx.Scenes.Replace(new MissionResultScene(result));
            return;
        }
        // The Abort Mission dialog takes the input (its own Esc cancels it).
        if (_confirmAbort || _confirmEnd)
        {
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

        if (Ctx.Input.WasKeyPressed(Keys.F2))
            _fogEnabled = !_fogEnabled;
        if (Ctx.Input.WasKeyPressed(Keys.F8))
            DebugTeleportEnemy(tile);
        if (Ctx.Input.WasKeyPressed(Keys.F6))
            DebugPickEnemy(tier: false);
        if (Ctx.Input.WasKeyPressed(Keys.F7))
            DebugPickEnemy(tier: true);
        if (Ctx.Input.WasKeyPressed(Keys.F10))
            DebugSpawnEnemy(tile);
        if (Ctx.Input.WasKeyPressed(Keys.F9))
            Console.WriteLine(_engine.Log.ToJson());

        var soldiers = Soldiers();
        if (Ctx.Bindings.WasPressed(GameAction.EndTurn))
            RequestEndTurn();
        else if (_exhaustedTimer >= 0f && (_exhaustedTimer -= dt) < 0f)
            Play(_engine.EndPlayerPhase());
        if (_autoSelect && !Busy)
            AutoSelect();
        if (_targeting is not null && !Busy)
            UpdateTargeting();
        else if (soldiers.Count > 0 && !Busy)
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
        // Summoned units join the view model during playback; they get a view the frame they appear.
        foreach (var vu in _vm.Units.Values)
            if (!_views.ContainsKey(vu.Id))
                _views[vu.Id] = new UnitView(World, Ctx.Assets, Ctx.Data, vu);
        foreach (var (id, view) in _views)
        {
            var unit = view.Unit;
            // Evacuated: gone from the map. Killed units stay as corpses.
            if (unit.Removed)
            {
                view.Destroy();
                _views.Remove(id);
                _vm.Units.Remove(id);
                continue;
            }
            var visible = ViewVisible(unit);
            view.Sync(
                selected: id == _selected,
                showOverlay: visible,
                time: _time,
                cover: id == _selected && !Busy ? StandingCover(_state.Unit(id)) : null,
                visible: visible
            );
        }
        var size = Ctx.Render.CanvasSize;
        _floatersView.Sync(_vm.Floaters, _camera.PixelsPerTile(size.Y));
        _effects.Sync(_vm.Effects);
        _effects.SyncSuppression(_vm.Units, _time, ViewVisible);
    }

    // What the view model shows is drawn when it is a friend, or an enemy the squad sees (or whose
    // action is being played). The tile is the view model's, so playback never peeks at the state.
    private bool ViewVisible(UnitVm unit) =>
        !unit.IsHostile
        || unit.ForceVisible
        || !_fogEnabled
        || _state.PlayerVision.IsVisible(unit.Tile);

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

    private TargetingMode? TryBeginTargeting(
        out string reason,
        string abilityId = AbilityRegistry.Shoot
    )
    {
        if (Selected is not { } shooter || IsMoving)
        {
            reason = "Select a soldier.";
            return null;
        }
        return TargetingMode.TryBegin(_engine, shooter, IsVisible, out reason, abilityId);
    }

    private void BeginTargeting(string abilityId = AbilityRegistry.Shoot)
    {
        if (TryBeginTargeting(out var reason, abilityId) is not { } mode)
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
            && (
                mode.TargetsAllies
                    ? clicked.Faction == Faction.Firewall
                    : clicked.Faction != Faction.Firewall
            )
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
        if (_vm.Units.TryGetValue(enemy.Id, out var shown))
            shown.SnapTo(to);
        _state.RefreshVision();
        InvalidateRange();
    }

    // Debug (F6 type, F7 tier): which enemy F10 spawns. Bosses and objects have no tiers.
    private void DebugPickEnemy(bool tier)
    {
        if (tier)
            _debugTier = (_debugTier + 1) % (Ctx.Data.Elites.Count + 1);
        else
            _debugEnemy = (_debugEnemy + 1) % _debugEnemies.Length;
        Ctx.Ui.Toast($"Debug spawn (F10): {Ctx.Data.Enemies[DebugEnemyId()].Name}");
    }

    private string DebugEnemyId()
    {
        var id = _debugEnemies[_debugEnemy];
        return _debugTier > 0 && EliteTransform.HasVariants(Ctx.Data.Enemies[id])
            ? EliteTransform.VariantId(id, Ctx.Data.Elites[_debugTier - 1])
            : id;
    }

    // Debug (F10): puts the chosen enemy, awake, on the hovered free tile.
    private void DebugSpawnEnemy(GridPos? tile)
    {
        if (tile is not { } at || _state.IsOccupied(at) || Busy)
            return;
        var def = Ctx.Data.Enemies[DebugEnemyId()];
        if (!_state.Map.IsWalkable(at) && def.Kind != EnemyKind.Object)
            return;
        var unit = PodSpawner.CreateUnit(_state, def, def.Name);
        unit.Position = at;
        _state.AddUnit(unit);
        _state.RefreshVision();
        var vu = new UnitVm(unit, def.Robot, UnitPresentation.InitialFacing(unit, _state));
        _vm.Units[unit.Id] = vu;
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
    private bool IsMoving => _player.IsBusy;

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

    // Plays queued events at the chosen speed (Space held fast-forwards) and tidies up after them.
    private void UpdatePlayback(float dt)
    {
        _player.Speed = Ctx.Playback.Effective(Ctx.Input.IsKeyPressed(Keys.Space));
        var wasBusy = _player.IsBusy;
        _player.Update(dt);
        if (!wasBusy || _player.IsBusy)
            return;
        InvalidateRange();
        if (Selected is { } sel)
            _director.Return(TacticalCamera.TileCenter(sel.Position.X, sel.Position.Y));
    }

    // The scene's share of the events: things that are not drawn but must happen when playback gets there.
    private void OnSceneEvent(GameEvent e)
    {
        switch (e)
        {
            case MoveBlocked:
                Ctx.Ui.Toast("Something is in the way.");
                break;
            case UnitBleedingOut down:
                Ctx.Ui.Toast(
                    $"{_state.Unit(down.Unit).DisplayName} is bleeding out! {Casualties.BleedOutTurns} turns to stabilize.",
                    4f
                );
                break;
            case UnitEvacuated evacuated:
                Ctx.Ui.Toast($"{_state.Unit(evacuated.Unit).DisplayName} evacuated.");
                break;
            case MissionEnded:
                _resultTimer = ResultDelaySeconds;
                break;
            case AllSoldiersExhausted:
                // End the turn after a short beat.
                _exhaustedTimer = 0.8f;
                break;
        }
    }

    // Hands the events to the player. The state has already moved on; the view model catches up.
    private void Play(IReadOnlyList<GameEvent> events)
    {
        InvalidateRange();
        _targeting = null;
        _exhaustedTimer = -1f;
        _player.Enqueue(events);
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
        DrawPodBanner(canvas, ui);
        if (_confirmAbort)
            DrawAbortDialog(ui);
        else if (_confirmEnd)
            DrawEndTurnDialog(ui);
    }

    // ------------------------------------------------------------------ HUD glue

    /// <summary>
    /// Playback or the enemy phase is running (or the mission is over): input is locked. The rules run
    /// the enemy phase inside the End Turn command, so the state is already back in the player phase
    /// while its banners play; only the closing player-phase banner lets input through.
    /// </summary>
    private bool Busy =>
        IsMoving || _state.Phase != TacticalPhase.Player || _state.Result is not null;

    private IReadOnlyList<AbilitySlot> HudSlots() =>
        Selected is { } unit
            ? HudModel.AbilitySlots(unit, Ctx.Data, OnEvacTile, StabilizeCommand() is not null)
            : [];

    // Why the slot cannot be used right now; null when it can.
    private string? SlotBlocked(AbilitySlot slot)
    {
        if (slot.AbilityId == AbilityRegistry.DebugStabilize)
            return StabilizeCommand() is not { } cmd
                    ? "No bleeding-out squadmate next to this soldier."
                : IsMoving ? "Wait for the move to finish."
                : _engine.Validate(cmd) is { IsValid: false } bad ? bad.Reason
                : null;
        return Ctx.Data.Abilities[slot.AbilityId].Targeting switch
        {
            // Armed targeting can always be left again by pressing the button once more.
            TargetingType.Enemy or TargetingType.Ally => _targeting is not null
            || TryBeginTargeting(out var reason, slot.AbilityId) is not null
                ? null
                : reason,
            TargetingType.Self => SelfAbilityBlocked(slot.AbilityId),
            _ => "This ability is always on.",
        };
    }

    private HudFrame BuildHud()
    {
        var banner = _vm.Banner;
        var animated = banner is not null;
        return new HudFrame
        {
            Squad = _state.Units.Where(u => u.Faction == Faction.Firewall).ToList(),
            Selected = Selected,
            Abilities = HudSlots().Select(s => new HudAbility(s, SlotBlocked(s))).ToList(),
            ArmedAbility = _targeting?.AbilityId,
            Enemies = VisibleEnemies(),
            Boss = ActiveBoss(),
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
                banner is not null ? HudModel.PhaseText(banner.Phase, banner.Turn)
                : _state.Result is { } result ? MissionResultText.Title(result.Outcome)
                : null,
            BannerTime = banner?.Elapsed ?? 0f,
            BannerSeconds = ViewModel.BannerSeconds,
            BannerStatic = !animated,
            Time = _time,
        };
    }

    // The first living boss the squad sees or that has been woken; its HP is the playback's.
    private HudBoss? ActiveBoss()
    {
        foreach (var u in _state.Units)
        {
            if (
                u is not { Faction: Faction.Meridian, IsOnMap: true, IsActive: true }
                || Ctx.Data.Enemies[u.DefId].Behavior.Kind != AiKind.Boss
                || !_vm.Units.TryGetValue(u.Id, out var shown)
                || shown.Dead
                || !ViewVisible(shown)
            )
                continue;
            return new HudBoss(u.DisplayName, shown.Hp, shown.MaxHp);
        }
        return null;
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
                _targeting?.Candidates.FirstOrDefault(c => c.Unit.Id == u.Id)?.Preview?.HitChance,
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
        if (abilityId == AbilityRegistry.DebugStabilize)
        {
            Stabilize();
            return;
        }
        switch (Ctx.Data.Abilities[abilityId].Targeting)
        {
            case TargetingType.Enemy or TargetingType.Ally:
                // The same button again cancels; another aimed ability swaps over.
                var armed = _targeting?.AbilityId;
                if (armed is not null)
                    ExitTargeting();
                if (armed != abilityId)
                    BeginTargeting(abilityId);
                break;
            case TargetingType.Self:
                UseSelfAbility(abilityId);
                break;
            default:
                Ctx.Ui.Toast("This ability is always on.");
                break;
        }
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
        foreach (var (_, view) in _views)
        {
            var unit = view.Unit;
            if (!unit.Statuses.IsBleedingOut || !ViewVisible(unit))
                continue;
            var anchor = new Vector2(unit.Center.X, view.IconRowBottom + 0.45f);
            var at = ui.Space.FromCanvasPixels(
                Picking.NdcToCanvas(Vector2.Transform(anchor, vp), size)
            );
            var text = unit.Statuses.BleedingOutTurnsLeft.ToString();
            canvas.Text(text, at - new Vector2(canvas.Measure(text, style) / 2f, 10f), style);
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
        if (_vm.PodBanner is not { } name)
            return;
        var width = canvas.Measure(name, TextStyles.Heading) + 64f;
        var pos = new Vector2(UiSpace.LogicalWidth / 2f - width / 2f, ui.Space.CanvasRect.Y + 140f);
        canvas.FillRect(pos, new Vector2(width, 48f), new Vector4(0.45f, 0.06f, 0.06f, 0.85f));
        canvas.Text(name, new Vector2(pos.X + 32f, pos.Y + 10f), TextStyles.Heading);
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
