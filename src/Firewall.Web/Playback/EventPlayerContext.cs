using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Tactical;

namespace Firewall.Web.Playback;

/// <summary>Where playback wants the camera. Implementations decide whether a move is needed at all.</summary>
public interface ICameraDirector
{
    /// <summary>Brings a world point into a comfortable part of the view.</summary>
    void Focus(Vector2 world, float seconds = 0.35f);

    /// <summary>Brings both points into view, centred between them if either is off-centre.</summary>
    void Frame(Vector2 a, Vector2 b, float seconds = 0.35f);
}

/// <summary>A director that never moves the camera.</summary>
public sealed class NullCameraDirector : ICameraDirector
{
    public void Focus(Vector2 world, float seconds) { }

    public void Frame(Vector2 a, Vector2 b, float seconds) { }
}

/// <summary>What animations may ask of the scene, without reaching into rules state.</summary>
public sealed class EventPlayerContext
{
    public ICameraDirector Camera { get; init; } = new NullCameraDirector();

    /// <summary>True if the squad can see the tile; hidden enemies must not leak what they do.</summary>
    public Func<GridPos, bool> IsTileVisible { get; init; } = _ => true;

    /// <summary>Told about events that are not drawn but matter to the scene (toasts, mission end, ...).</summary>
    public Action<GameEvent> SceneEvent { get; init; } = _ => { };

    /// <summary>The latest shot, so the damage that follows knows it was a crit.</summary>
    public ShotResolved? LastShot { get; set; }

    /// <summary>True when the player should watch this actor: soldiers always, enemies only when seen.</summary>
    public bool IsWatched(UnitVm unit, GridPos from, GridPos to) =>
        !unit.IsHostile || unit.ForceVisible || IsTileVisible(from) || IsTileVisible(to);
}
