using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Data;
using Firewall.Rules.Tactical;
using Firewall.Web.Units;
using Yaeger.Graphics;

namespace Firewall.Web.Playback;

/// <summary>What is left of a unit on the ground.</summary>
public enum CorpseKind
{
    None,

    /// <summary>The body sprite, darkened and rotated.</summary>
    Body,

    /// <summary>The "scrap" decal an enemy robot drops.</summary>
    Scrap,
}

/// <summary>
/// What the player sees of one unit. Playback changes these fields step by step while the rules
/// state has long since moved on, so views draw from here and never from the <see cref="TacticalUnit"/>.
/// </summary>
public sealed class UnitVm
{
    public UnitVm(TacticalUnit unit, bool robot, Vector2 facing)
    {
        Id = unit.Id;
        Faction = unit.Faction;
        DefId = unit.DefId;
        AppearanceId = unit.AppearanceId;
        Robot = robot;
        WeaponClass = unit.Weapon?.Def.Class;
        Tile = unit.Position;
        Center = TileCenter(unit.Position);
        Rotation = UnitPresentation.RotationFor(facing);
        Hp = unit.Hp;
        MaxHp = unit.MaxHp;
        Armor = unit.Armor;
        Statuses.Flags = unit.Statuses.Flags;
        Statuses.BleedingOutTurnsLeft = unit.Statuses.BleedingOutTurnsLeft;
    }

    public UnitId Id { get; }
    public Faction Faction { get; }
    public string DefId { get; }
    public string AppearanceId { get; }
    public bool Robot { get; }
    public WeaponClass? WeaponClass { get; }

    /// <summary>The tile the unit stands on once the current step has finished.</summary>
    public GridPos Tile { get; set; }

    /// <summary>Where the body is drawn, in world units.</summary>
    public Vector2 Center { get; set; }

    /// <summary>Body rotation in radians; sprites face +X at 0.</summary>
    public float Rotation { get; set; }

    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Armor { get; set; }
    public StatusSet Statuses { get; } = new();

    /// <summary>Opacity of the body, 0 to 1.</summary>
    public float Alpha { get; set; } = 1f;

    /// <summary>Brightness of the body: 1 normal, 0.4 for a corpse.</summary>
    public float Brightness { get; set; } = 1f;

    /// <summary>A colour the body is pulled toward (a damage flash), with <see cref="FlashAmount"/> from 0 to 1.</summary>
    public Color FlashColor { get; set; } = Color.White;

    public float FlashAmount { get; set; }

    /// <summary>Dying or dead: no pips, icons or selection ring.</summary>
    public bool Dead { get; set; }

    public CorpseKind Corpse { get; set; }

    /// <summary>Seconds of reload pose left.</summary>
    public float ReloadSeconds { get; set; }

    /// <summary>Drawn even if the squad cannot see the tile, while an animation shows what the unit does.</summary>
    public bool ForceVisible { get; set; }

    /// <summary>Left the map (evacuated): the view goes away.</summary>
    public bool Removed { get; set; }

    public bool IsHostile => Faction == Faction.Meridian;

    public void SnapTo(GridPos tile)
    {
        Tile = tile;
        Center = TileCenter(tile);
    }

    public void FaceToward(Vector2 from, Vector2 to)
    {
        var dir = to - from;
        if (dir != Vector2.Zero)
            Rotation = UnitPresentation.RotationFor(dir);
    }

    public static Vector2 TileCenter(GridPos p) => new(p.X + 0.5f, p.Y + 0.5f);
}

/// <summary>A short-lived text that rises and fades above a point in the world.</summary>
public sealed class FloaterVm(string text, Color color, int size, Vector2 anchor, int stackSlot)
{
    public const float Rise = 0.6f;
    public const float Seconds = 0.9f;

    /// <summary>Vertical gap between texts stacked on the same spot, in tiles.</summary>
    public const float StackGap = 0.32f;

    public string Text { get; } = text;
    public Color Color { get; } = color;
    public int Size { get; } = size;
    public Vector2 Anchor { get; } = anchor;
    public int StackSlot { get; } = stackSlot;
    public float Age { get; set; }

    public float Progress => Math.Clamp(Age / Seconds, 0f, 1f);

    /// <summary>World position of the text's centre now.</summary>
    public Vector2 Position => Anchor + new Vector2(0f, StackSlot * StackGap + Rise * Progress);

    /// <summary>Fully opaque for the first half, then fading out.</summary>
    public float Alpha => Math.Clamp(2f * (1f - Progress), 0f, 1f);
}

/// <summary>A tracer line, muzzle flash or flash quad.</summary>
public sealed class EffectVm(EffectKind kind, Vector2 from, Vector2 to, float seconds, Color color)
{
    public EffectKind Kind { get; } = kind;
    public Vector2 From { get; } = from;
    public Vector2 To { get; } = to;
    public float Seconds { get; } = seconds;
    public Color Color { get; } = color;

    /// <summary>Square size of a flash quad in tiles, or the width of a tracer.</summary>
    public float Size { get; init; } = 1f;

    public float Age { get; set; }
    public float Progress => Seconds <= 0f ? 1f : Math.Clamp(Age / Seconds, 0f, 1f);
}

public enum EffectKind
{
    Tracer,
    MuzzleFlash,
    FlashQuad,
}

/// <summary>The phase banner on screen.</summary>
public sealed record BannerVm(TacticalPhase Phase, int Turn)
{
    public float Elapsed { get; set; }
}

/// <summary>
/// Everything playback draws. Starts as a copy of the rules state, then only events change it.
/// </summary>
public sealed class ViewModel
{
    public const float BannerSeconds = 0.9f;

    private readonly List<FloaterVm> _floaters = [];
    private readonly List<EffectVm> _effects = [];

    public Dictionary<UnitId, UnitVm> Units { get; } = [];

    public IReadOnlyList<FloaterVm> Floaters => _floaters;
    public IReadOnlyList<EffectVm> Effects => _effects;

    public BannerVm? Banner { get; set; }

    /// <summary>The pod name shown while a pod wakes up.</summary>
    public string? PodBanner { get; set; }

    public static ViewModel FromState(TacticalState state, GameData data)
    {
        var vm = new ViewModel();
        foreach (var unit in state.Units)
        {
            if (!unit.IsOnMap && unit.IsAlive)
                continue;
            var robot = unit.Faction == Faction.Meridian && data.Enemies[unit.DefId].Robot;
            var vu = new UnitVm(unit, robot, UnitPresentation.InitialFacing(unit, state));
            if (!unit.IsAlive)
            {
                vu.Dead = true;
                vu.Corpse = robot ? CorpseKind.Scrap : CorpseKind.Body;
            }
            vm.Units[unit.Id] = vu;
        }
        return vm;
    }

    /// <summary>Adds a text above <paramref name="anchor"/>, stacked over any still showing there.</summary>
    public FloaterVm AddFloater(string text, Color color, int size, Vector2 anchor)
    {
        var slot = _floaters.Count(f => Vector2.DistanceSquared(f.Anchor, anchor) < 0.25f);
        var floater = new FloaterVm(text, color, size, anchor, slot);
        _floaters.Add(floater);
        return floater;
    }

    public EffectVm AddEffect(EffectVm effect)
    {
        _effects.Add(effect);
        return effect;
    }

    /// <summary>Ages floaters, effects, the banner and the reload pose by <paramref name="dt"/> playback seconds.</summary>
    public void Update(float dt)
    {
        foreach (var f in _floaters)
            f.Age += dt;
        _floaters.RemoveAll(f => f.Age >= FloaterVm.Seconds);
        foreach (var e in _effects)
            e.Age += dt;
        _effects.RemoveAll(e => e.Age >= e.Seconds);
        foreach (var u in Units.Values)
            if (u.ReloadSeconds > 0f)
                u.ReloadSeconds = Math.Max(0f, u.ReloadSeconds - dt);
        if (Banner is { } banner && (banner.Elapsed += dt) >= BannerSeconds)
            Banner = null;
    }
}
