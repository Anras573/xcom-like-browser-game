using System.Numerics;
using Firewall.Rules.Data;
using Firewall.Rules.Tactical;
using Firewall.Web.Assets;
using Firewall.Web.Rendering;
using Yaeger.ECS;
using Yaeger.Graphics;

namespace Firewall.Web.Units;

/// <summary>
/// The entities that show one <see cref="TacticalUnit"/>: body sprite, selection ring, HP pips and
/// status icons. Reads the unit; never changes it. Call <see cref="Sync"/> every frame.
/// </summary>
public sealed class UnitView
{
    private const float PipWidth = 0.13f;
    private const float PipHeight = 0.09f;
    private const float PipGap = 0.03f;
    private const int PipsPerRow = 10;
    private const float IconSize = 0.3f;

    private static readonly Color SoldierPip = new(90, 220, 120);
    private static readonly Color EnemyPip = new(235, 80, 70);
    private static readonly Color EmptyPip = new(30, 30, 36, 190);
    private static readonly Color ArmorPip = new(165, 175, 190);
    private static readonly Color Hidden = new(255, 255, 255, 0);

    private readonly World _world;
    private readonly AssetRegistry _assets;
    private readonly EnemyDef? _enemy;
    private readonly Color? _tint;
    private readonly float _scale;
    private readonly Entity _body;
    private readonly Entity _ring;
    private readonly List<Entity> _pips = [];
    private readonly Entity[] _icons;
    private string _frameName = "";

    public UnitView(
        World world,
        AssetRegistry assets,
        GameData data,
        TacticalUnit unit,
        Vector2 facing
    )
    {
        _world = world;
        _assets = assets;
        Unit = unit;
        _enemy = unit.Faction == Faction.Meridian ? data.Enemies[unit.DefId] : null;
        _tint = UnitPresentation.ParseTint(_enemy?.Sprite.Tint);
        _scale = (float)(_enemy?.Sprite.Scale ?? 1.0);
        Rotation = UnitPresentation.RotationFor(facing);

        _body = world.CreateEntity();
        world.AddComponent(_body, RenderLayers.Of(RenderLayers.Units));

        _ring = world.CreateEntity();
        var ring = assets.Frame("overlay.selection_ring");
        world.AddComponent(_ring, ring.ToSpriteSheet(Hidden));
        world.AddComponent(_ring, new AnimationState(ring.FrameIndex));
        world.AddComponent(_ring, RenderLayers.Of(RenderLayers.Highlights));

        _icons = new Entity[3];
        for (var i = 0; i < _icons.Length; i++)
        {
            _icons[i] = world.CreateEntity();
            world.AddComponent(_icons[i], RenderLayers.Of(RenderLayers.UnitOverlay));
        }
    }

    public TacticalUnit Unit { get; }

    /// <summary>Body rotation in radians; sprites face +X at 0.</summary>
    public float Rotation { get; set; }

    /// <summary>Where to draw the unit instead of its tile, e.g. mid-move tween; null draws it on its tile.</summary>
    public Vector2? VisualCenter { get; set; }

    /// <summary>True for the frame(s) the unit plays its reload pose.</summary>
    public bool Reloading { get; set; }

    public void FaceToward(Vector2 dir) => Rotation = UnitPresentation.RotationFor(dir);

    /// <param name="selected">Draw the pulsing selection ring.</param>
    /// <param name="showOverlay">Draw HP pips and status icons (soldiers always, enemies when seen).</param>
    /// <param name="time">Scene time in seconds, for the ring pulse.</param>
    public void Sync(bool selected, bool showOverlay, float time)
    {
        var unit = Unit;
        var center = VisualCenter ?? TacticalCamera.TileCenter(unit.Position.X, unit.Position.Y);

        var frameName = UnitPresentation.BodyFrame(unit, _enemy, Reloading);
        var frame = _assets.Frame(frameName);
        if (frameName != _frameName)
        {
            _frameName = frameName;
            _world.AddComponent(_body, frame.ToSpriteSheet(_tint));
            _world.AddComponent(_body, new AnimationState(frame.FrameIndex));
        }
        _world.AddComponent(_body, new Transform2D(center, Rotation, new Vector2(_scale, _scale)));

        var ringScale = 1.1f * _scale;
        _world.AddComponent(_ring, new Transform2D(center, 0f, new Vector2(ringScale, ringScale)));
        var ring = _assets.Frame("overlay.selection_ring");
        var alpha = (byte)(255 * UnitPresentation.RingAlpha(time));
        _world.AddComponent(
            _ring,
            ring.ToSpriteSheet(selected ? new Color(255, 255, 255, alpha) : Hidden)
        );

        var top = center.Y + 0.5f * _scale + 0.08f;
        SyncPips(center.X, top, showOverlay);
        SyncIcons(center.X, top + PipRows() * (PipHeight + PipGap) + 0.05f, showOverlay);
    }

    private int PipRows() => Math.Max(1, (Unit.MaxHp + Unit.Armor + PipsPerRow - 1) / PipsPerRow);

    private void SyncPips(float centerX, float top, bool show)
    {
        var pips = UnitPresentation.Pips(Unit.Hp, Unit.MaxHp, Unit.Armor);
        // Entities are only added, so they survive a max-HP or armor change.
        while (_pips.Count < pips.Count)
            _pips.Add(_world.CreateEntity());
        var filled = Unit.Faction == Faction.Meridian ? EnemyPip : SoldierPip;

        for (var i = 0; i < _pips.Count; i++)
        {
            if (i >= pips.Count || !show)
            {
                _world.AddComponent(_pips[i], new Sprite("", Hidden));
                _world.AddComponent(_pips[i], RenderLayers.Of(RenderLayers.UnitOverlay));
                _world.AddComponent(_pips[i], new Transform2D(Vector2.Zero));
                continue;
            }
            var row = i / PipsPerRow;
            var inRow = Math.Min(PipsPerRow, pips.Count - row * PipsPerRow);
            var rowWidth = inRow * PipWidth + (inRow - 1) * PipGap;
            var x =
                centerX - rowWidth / 2f + (i % PipsPerRow) * (PipWidth + PipGap) + PipWidth / 2f;
            var y = top + row * (PipHeight + PipGap) + PipHeight / 2f;
            var color = pips[i] switch
            {
                PipKind.Filled => filled,
                PipKind.Armor => ArmorPip,
                _ => EmptyPip,
            };
            _world.AddComponent(_pips[i], new Sprite("", color));
            _world.AddComponent(
                _pips[i],
                new Transform2D(new Vector2(x, y), 0f, new Vector2(PipWidth, PipHeight))
            );
            _world.AddComponent(_pips[i], RenderLayers.Of(RenderLayers.UnitOverlay));
        }
    }

    private void SyncIcons(float centerX, float bottom, bool show)
    {
        var names = UnitPresentation.StatusIcons(Unit.Statuses);
        for (var i = 0; i < _icons.Length; i++)
        {
            if (!show || i >= names.Count)
            {
                var any = _assets.Frame("status.overwatch");
                _world.AddComponent(_icons[i], any.ToSpriteSheet(Hidden));
                _world.AddComponent(_icons[i], new AnimationState(any.FrameIndex));
                _world.AddComponent(_icons[i], new Transform2D(Vector2.Zero));
                continue;
            }
            var frame = _assets.Frame(names[i]);
            var rowWidth = names.Count * IconSize;
            var x = centerX - rowWidth / 2f + i * IconSize + IconSize / 2f;
            _world.AddComponent(_icons[i], frame.ToSpriteSheet());
            _world.AddComponent(_icons[i], new AnimationState(frame.FrameIndex));
            _world.AddComponent(
                _icons[i],
                new Transform2D(
                    new Vector2(x, bottom + IconSize / 2f),
                    0f,
                    new Vector2(IconSize, IconSize)
                )
            );
        }
    }

    public void Destroy()
    {
        _world.DestroyEntity(_body);
        _world.DestroyEntity(_ring);
        foreach (var e in _pips)
            _world.DestroyEntity(e);
        foreach (var e in _icons)
            _world.DestroyEntity(e);
        _pips.Clear();
    }
}
