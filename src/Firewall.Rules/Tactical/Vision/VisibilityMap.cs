namespace Firewall.Rules.Tactical.Vision;

public enum TileVisibility
{
    Unexplored,
    Explored,
    Visible,
}

/// <summary>Where a faction last saw a unit, and on which turn.</summary>
public readonly record struct LastKnown(GridPos Position, int Turn);

/// <summary>
/// What one faction sees (GDD §4.4): per tile unexplored / explored / visible, and which hostile
/// units are in view. Shared by the whole faction. <see cref="Recompute"/> is cheap to call after
/// every step: each viewer's visible tiles are cached by position and sight, and the cache is
/// dropped when <see cref="TacticalState.MapVersion"/> moves.
/// </summary>
public sealed class VisibilityMap
{
    private readonly TacticalState _state;
    private readonly TileVisibility[] _tiles;
    private readonly Dictionary<(GridPos Pos, int Sight), GridPos[]> _cache = [];
    private readonly Dictionary<UnitId, LastKnown> _lastKnown = [];
    private int _cacheVersion;
    private HashSet<UnitId> _visibleEnemies = [];

    public VisibilityMap(TacticalState state, Faction faction)
    {
        _state = state;
        Faction = faction;
        _tiles = new TileVisibility[state.Map.Width * state.Map.Height];
    }

    public Faction Faction { get; }

    public TileVisibility this[GridPos pos] =>
        _state.Map.InBounds(pos)
            ? _tiles[pos.Y * _state.Map.Width + pos.X]
            : TileVisibility.Unexplored;

    public bool IsVisible(GridPos pos) => this[pos] == TileVisibility.Visible;

    /// <summary>Own and neutral units always count as seen; hostile ones need a visible tile.</summary>
    public bool IsUnitVisible(TacticalUnit unit) =>
        !IsHostile(unit) || (unit.IsOnMap && IsVisible(unit.Position));

    /// <summary>Hostile living units in view right now.</summary>
    public IReadOnlyCollection<UnitId> VisibleEnemies => _visibleEnemies;

    /// <summary>
    /// Hostile units not in view now but seen on <paramref name="turn"/> or the turn before, at the
    /// place they were last seen. The AI (#26) plans against these.
    /// </summary>
    public IEnumerable<KeyValuePair<UnitId, LastKnown>> LastKnownEnemies(int turn) =>
        _lastKnown
            .Where(kv => kv.Value.Turn >= turn - 1 && !_visibleEnemies.Contains(kv.Key))
            .OrderBy(kv => kv.Key.Value);

    /// <summary>
    /// Re-evaluates the map from the current unit positions and tiles. Returns the
    /// <see cref="VisibilityChanged"/> event when the set of visible enemies changed, else null.
    /// </summary>
    public VisibilityChanged? Recompute()
    {
        var map = _state.Map;
        for (var i = 0; i < _tiles.Length; i++)
            if (_tiles[i] == TileVisibility.Visible)
                _tiles[i] = TileVisibility.Explored;

        foreach (var viewer in _state.Units)
        {
            if (viewer.Faction != Faction || !viewer.IsOnMap || viewer.Statuses.IsDown)
                continue;
            foreach (var pos in VisibleFrom(viewer.Position, viewer.Sight))
                _tiles[pos.Y * map.Width + pos.X] = TileVisibility.Visible;
        }

        var now = new HashSet<UnitId>();
        foreach (var unit in _state.Units)
        {
            if (!IsHostile(unit))
                continue;
            if (!unit.IsOnMap)
            {
                _lastKnown.Remove(unit.Id);
                continue;
            }
            if (!IsVisible(unit.Position))
                continue;
            now.Add(unit.Id);
            _lastKnown[unit.Id] = new LastKnown(unit.Position, _state.TurnNumber);
        }

        var shown = now.Where(id => !_visibleEnemies.Contains(id)).OrderBy(id => id.Value).ToList();
        var hidden = _visibleEnemies
            .Where(id => !now.Contains(id))
            .OrderBy(id => id.Value)
            .ToList();
        _visibleEnemies = now;
        return shown.Count + hidden.Count > 0 ? new VisibilityChanged(shown, hidden) : null;
    }

    private GridPos[] VisibleFrom(GridPos origin, int sight)
    {
        // The sight lines depend on the tiles: a changed map (MapVersion) drops them all.
        if (_cacheVersion != _state.MapVersion)
        {
            _cache.Clear();
            _cacheVersion = _state.MapVersion;
        }
        if (_cache.TryGetValue((origin, sight), out var cached))
            return cached;
        var map = _state.Map;
        var result = new List<GridPos>();
        for (
            var y = Math.Max(0, origin.Y - sight);
            y <= Math.Min(map.Height - 1, origin.Y + sight);
            y++
        )
        for (
            var x = Math.Max(0, origin.X - sight);
            x <= Math.Min(map.Width - 1, origin.X + sight);
            x++
        )
        {
            var pos = new GridPos(x, y);
            if (LosTracer.CanSee(map, origin, sight, pos))
                result.Add(pos);
        }
        return _cache[(origin, sight)] = [.. result];
    }

    private bool IsHostile(TacticalUnit unit) =>
        unit.Faction != Faction && unit.Faction != Faction.Neutral && Faction != Faction.Neutral;
}
