using Firewall.Rules;
using Firewall.Rules.Tactical;
using Firewall.Web.Rendering;

namespace Firewall.Web.Units;

/// <summary>
/// Targeting for a tile-aimed item (Frag Grenade, Smoke, Lace Jammer, Breach Charge): the tiles the
/// thrower can reach (range and line of sight, the ring), and for the tile under the cursor what the
/// blast would do (<see cref="BlastPreview"/>). Built from the engine, and stale as soon as the map
/// changes (<see cref="TacticalState.MapVersion"/>), because that changes the line of sight.
/// </summary>
public sealed class TileTargetingMode
{
    private readonly TacticalEngine _engine;
    private readonly HashSet<GridPos> _throwable;
    private readonly int _mapVersion;
    private GridPos? _previewTile;
    private BlastPreview? _preview;

    private TileTargetingMode(TacticalEngine engine, UnitId user, string abilityId)
    {
        _engine = engine;
        User = user;
        AbilityId = abilityId;
        _mapVersion = engine.State.MapVersion;
        Throwable = engine.ThrowableTiles(engine.State.Unit(user), abilityId);
        _throwable = [.. Throwable];
        RangeFill = TileRegions.FillRuns(Throwable);
        RangeOutline = TileRegions.Outline(Throwable, 0.05f);
    }

    public UnitId User { get; }

    public string AbilityId { get; }

    /// <summary>Every tile the item can be thrown at right now.</summary>
    public IReadOnlyList<GridPos> Throwable { get; }

    public IReadOnlyList<TileRect> RangeFill { get; }

    public IReadOnlyList<TileRect> RangeOutline { get; }

    /// <summary>The map changed since the ring was built; the owner should rebuild the mode.</summary>
    public bool IsStale => _engine.State.MapVersion != _mapVersion;

    public bool CanTarget(GridPos tile) => _throwable.Contains(tile);

    /// <summary>The blast the item would make at <paramref name="tile"/>; null when it cannot be thrown there.</summary>
    public BlastPreview? PreviewAt(GridPos? tile)
    {
        if (tile != _previewTile)
        {
            _previewTile = tile;
            _preview =
                tile is { } t && CanTarget(t)
                    ? _engine.PreviewBlast(_engine.State.Unit(User), AbilityId, t)
                    : null;
        }
        return _preview;
    }

    public UseAbilityCommand CommandFor(GridPos tile) => new(User, AbilityId, Target.Tile(tile));

    /// <summary>Enters tile targeting, or says why the button is disabled.</summary>
    public static TileTargetingMode? TryBegin(
        TacticalEngine engine,
        TacticalUnit user,
        string abilityId,
        out string reason
    )
    {
        var mode = new TileTargetingMode(engine, user.Id, abilityId);
        if (mode.Throwable.Count > 0)
        {
            reason = "";
            return mode;
        }
        // No tile qualifies: the engine's reason for the unit's own tile says why when it is AP,
        // charges or the like; otherwise nothing is in range.
        var own = engine.Validate(
            new UseAbilityCommand(user.Id, abilityId, Target.Tile(user.Position))
        );
        reason =
            own.IsValid
            || own.Reason.StartsWith("Too close")
            || own.Reason.StartsWith("Out of range")
                ? "No tile in range."
                : own.Reason;
        return null;
    }
}
