using System.Numerics;
using Firewall.Rules.Data;
using Firewall.Rules.Tactical;
using Firewall.Web.Assets;
using Firewall.Web.Playback;
using Firewall.Web.Rendering;
using Yaeger.ECS;
using Yaeger.Graphics;

namespace Firewall.Web.Units;

/// <summary>
/// The entities that show one <see cref="TacticalUnit"/>: body sprite, selection ring, HP pips and
/// status icons. Draws from the <see cref="UnitVm"/>, which playback keeps in step with the events,
/// and never reads the rules state. Call <see cref="Sync"/> every frame.
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
    private Color _bodyColor;
    private bool _bodyColorSet;
    private bool _onGround;

    public UnitView(World world, AssetRegistry assets, GameData data, UnitVm unit)
    {
        _world = world;
        _assets = assets;
        Unit = unit;
        _enemy = unit.IsHostile ? data.Enemies[unit.DefId] : null;
        _tint = UnitPresentation.ParseTint(_enemy?.Sprite.Tint);
        _scale = (float)(_enemy?.Sprite.Scale ?? 1.0);

        _body = world.CreateEntity();
        world.AddComponent(_body, RenderLayers.Of(RenderLayers.Units));

        _ring = world.CreateEntity();
        var ring = assets.Frame("overlay.selection_ring");
        world.AddComponent(_ring, ring.ToSpriteSheet(Hidden));
        world.AddComponent(_ring, new AnimationState(ring.FrameIndex));
        world.AddComponent(_ring, RenderLayers.Of(RenderLayers.Highlights));

        _icons = new Entity[4];
        for (var i = 0; i < _icons.Length; i++)
        {
            _icons[i] = world.CreateEntity();
            world.AddComponent(_icons[i], RenderLayers.Of(RenderLayers.UnitOverlay));
        }
    }

    public UnitVm Unit { get; }

    /// <param name="selected">Draw the pulsing selection ring.</param>
    /// <param name="showOverlay">Draw HP pips and status icons (soldiers always, enemies when seen).</param>
    /// <param name="time">Scene time in seconds, for the ring pulse.</param>
    /// <param name="visible">False hides the whole unit (a hostile on a tile the squad cannot see).</param>
    /// <param name="cover">Cover against the most threatening visible enemy; draws a shield for half or full cover.</param>
    public void Sync(
        bool selected,
        bool showOverlay,
        float time,
        CoverResult? cover = null,
        bool visible = true
    )
    {
        var unit = Unit;
        var center = unit.Center;
        var scrap = unit.Corpse == CorpseKind.Scrap;
        var scale = scrap ? 1f : _scale;

        var frameName = scrap
            ? "decal.corpse_scrap"
            : UnitPresentation.BodyFrame(unit, _enemy, unit.ReloadSeconds > 0f);
        var color = visible ? BodyColor(unit) : Hidden;
        if (frameName != _frameName || !_bodyColorSet || !SameColor(color, _bodyColor))
        {
            _frameName = frameName;
            _bodyColor = color;
            _bodyColorSet = true;
            var frame = _assets.Frame(frameName);
            _world.AddComponent(_body, frame.ToSpriteSheet(color));
            _world.AddComponent(_body, new AnimationState(frame.FrameIndex));
        }
        // Corpses stay on the ground, under props and living units.
        var onGround = unit.Corpse != CorpseKind.None;
        if (onGround != _onGround)
        {
            _onGround = onGround;
            _world.AddComponent(
                _body,
                RenderLayers.Of(onGround ? RenderLayers.GroundDecal : RenderLayers.Units)
            );
        }
        _world.AddComponent(
            _body,
            new Transform2D(center, unit.Rotation, new Vector2(scale, scale))
        );

        var ringScale = 1.1f * _scale;
        _world.AddComponent(_ring, new Transform2D(center, 0f, new Vector2(ringScale, ringScale)));
        var ring = _assets.Frame("overlay.selection_ring");
        var alpha = (byte)(255 * UnitPresentation.RingAlpha(time));
        _world.AddComponent(
            _ring,
            ring.ToSpriteSheet(
                selected && visible && !unit.Dead ? new Color(255, 255, 255, alpha) : Hidden
            )
        );

        var top = center.Y + 0.5f * _scale + 0.08f;
        showOverlay &= visible && !unit.Dead;
        SyncPips(center.X, top, showOverlay);
        SyncIcons(center.X, IconRowBottom, showOverlay, cover, time);
    }

    // The tint the unit's own sprite has, darkened for corpses, pulled toward a flash, faded.
    private Color BodyColor(UnitVm unit)
    {
        var baseTint = _tint ?? Color.White;
        float Channel(byte c, byte flash) =>
            (c * unit.Brightness * (1f - unit.FlashAmount) + flash * unit.FlashAmount) / 255f;
        byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
        return new Color(
            ToByte(Channel(baseTint.R, unit.FlashColor.R)),
            ToByte(Channel(baseTint.G, unit.FlashColor.G)),
            ToByte(Channel(baseTint.B, unit.FlashColor.B)),
            ToByte(baseTint.A / 255f * unit.Alpha)
        );
    }

    private static bool SameColor(Color a, Color b) =>
        a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;

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

    /// <summary>World Y of the bottom of the status icon row (above the HP pips), for labels drawn above it.</summary>
    public float IconRowBottom =>
        Unit.Center.Y + 0.5f * _scale + 0.08f + PipRows() * (PipHeight + PipGap) + 0.05f;

    private void SyncIcons(float centerX, float bottom, bool show, CoverResult? cover, float time)
    {
        var names = new List<string>();
        if (cover is { } c && CoverShieldsView.FrameName(c.Level) is { } shield)
            names.Add(shield);
        names.AddRange(UnitPresentation.StatusIcons(Unit.Statuses));
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
            // Bleeding out pulses red so it reads at a glance.
            var tint =
                names[i] == "status.bleeding"
                    ? new Color(255, 70, 70, (byte)(255 * UnitPresentation.BleedPulseAlpha(time)))
                    : (Color?)null;
            _world.AddComponent(_icons[i], frame.ToSpriteSheet(tint));
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
