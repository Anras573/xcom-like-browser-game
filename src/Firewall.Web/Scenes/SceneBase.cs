using System.Numerics;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Yaeger.ECS;
using Yaeger.Graphics;
using Yaeger.Platform;

namespace Firewall.Web.Scenes;

/// <summary>
/// Plumbing shared by scenes: an own <see cref="World"/> (with a camera entity), a
/// <see cref="FrameRenderer"/> over it, and UI begin/end around <see cref="DrawScreen"/>.
/// </summary>
public abstract class SceneBase : IScene
{
    private World _world = new();
    private FrameRenderer? _frame;
    private Entity _cameraEntity;
    private float _dt;

    public abstract string Name { get; }
    public virtual bool IsOverlay => false;

    protected SceneContext Ctx { get; private set; } = null!;
    protected World World => _world;
    protected float DeltaTime => _dt;

    /// <summary>The world camera for this scene; the default shows the origin.</summary>
    protected virtual Camera2D Camera => new(Vector2.Zero, TacticalCamera.ZoomFor(11f));

    public int EntityCount => _world.Entities.Count();

    public void Enter(SceneContext ctx)
    {
        Ctx = ctx;
        _world = new World();
        _cameraEntity = _world.CreateEntity("camera");
        _world.AddComponent(_cameraEntity, Camera);
        OnEnter();
        _frame = ctx.Render?.CreateFrame(_world);
        if (_frame is not null)
        {
            _frame.WorldPass = DrawWorld;
            _frame.ScreenPass = DrawCanvas;
        }
    }

    public void Update(float dt)
    {
        _dt = dt;
        OnUpdate(dt);
        _world.AddComponent(_cameraEntity, Camera);
    }

    public void Render()
    {
        if (IsOverlay)
            _frame?.RenderScreenOnly();
        else
            _frame?.Render();
    }

    public void Exit()
    {
        OnExit();
        foreach (var e in _world.Entities.ToList())
            _world.DestroyEntity(e);
    }

    /// <summary>
    /// The screen pass. The default wraps <see cref="DrawScreen"/> in <c>Ui.Begin/End</c>; the boot
    /// scene overrides it because the UI toolkit doesn't exist until loading is done.
    /// </summary>
    protected virtual void DrawCanvas(ScreenCanvas canvas)
    {
        Ctx.Ui.Begin(canvas, new UiSpace(Ctx.Render.CanvasSize), _dt);
        DrawScreen(canvas, Ctx.Ui);
        Ctx.Ui.End();
    }

    /// <summary>Build entities and load resources.</summary>
    protected virtual void OnEnter() { }

    protected virtual void OnUpdate(float dt) { }

    protected virtual void OnExit() { }

    /// <summary>World-space immediate drawing (highlights, previews).</summary>
    protected virtual void DrawWorld(WorldOverlay overlay) { }

    /// <summary>Screen-space UI, between <c>Ui.Begin</c> and <c>Ui.End</c>.</summary>
    protected virtual void DrawScreen(ScreenCanvas canvas, UiContext ui) { }
}
