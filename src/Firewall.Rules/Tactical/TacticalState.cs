using Firewall.Rules.Rng;
using Firewall.Rules.Tactical.Vision;

namespace Firewall.Rules.Tactical;

/// <summary>Progress of the mission goal; the objective rules fill this in with the mission types.</summary>
public sealed class ObjectiveState(string missionId)
{
    public string MissionId { get; } = missionId;
    public int Progress { get; set; }
    public int Target { get; set; }
    public bool Complete { get; set; }
}

/// <summary>
/// Everything a tactical mission is made of: map, units, turn, RNG, objective and what each side
/// sees. Visibility is derived from the rest, so it is not part of <see cref="StateHash"/>.
/// </summary>
public sealed class TacticalState(TacticalMap map, IRandom rng, string missionId = "purge")
{
    private readonly List<TacticalUnit> _units = [];
    private readonly List<Pod> _pods = [];
    private int _nextUnitId = 1;
    private int _nextPodId = 1;
    private VisibilityMap? _playerVision;
    private VisibilityMap? _enemyVision;

    public TacticalMap Map { get; } = map;
    public IRandom Rng { get; } = rng;
    public ObjectiveState Objective { get; } = new(missionId);
    public int TurnNumber { get; set; } = 1;
    public TacticalPhase Phase { get; set; } = TacticalPhase.Player;

    /// <summary>Null while the mission is running.</summary>
    public MissionResult? Result { get; set; }

    /// <summary>Every unit, living or dead, in the order they were added.</summary>
    public IReadOnlyList<TacticalUnit> Units => _units;

    public IReadOnlyList<Pod> Pods => _pods;

    /// <summary>What the squad sees (fog of war).</summary>
    public VisibilityMap PlayerVision =>
        _playerVision ??= new VisibilityMap(this, Faction.Firewall);

    /// <summary>What Meridian sees; with its last-known soldier positions, the AI's knowledge.</summary>
    public VisibilityMap EnemyVision => _enemyVision ??= new VisibilityMap(this, Faction.Meridian);

    /// <summary>What <paramref name="faction"/> sees; null for neutrals, which see everything as it is.</summary>
    public VisibilityMap? VisionOf(Faction faction) =>
        faction switch
        {
            Faction.Firewall => PlayerVision,
            Faction.Meridian => EnemyVision,
            _ => null,
        };

    /// <summary>
    /// Recomputes both sides' vision. Returns the squad's <see cref="VisibilityChanged"/> when the
    /// set of visible enemies changed.
    /// </summary>
    public VisibilityChanged? RefreshVision()
    {
        EnemyVision.Recompute();
        return PlayerVision.Recompute();
    }

    public UnitId NextUnitId() => new(_nextUnitId++);

    public PodId AddPod(string name, GridPos spawn)
    {
        var id = new PodId(_nextPodId++);
        _pods.Add(new Pod(id, name, spawn));
        return id;
    }

    public void AddUnit(TacticalUnit unit)
    {
        if (_units.Exists(u => u.Id == unit.Id))
            throw new ArgumentException($"Unit {unit.Id} is already in the state.", nameof(unit));
        _units.Add(unit);
    }

    public TacticalUnit Unit(UnitId id) =>
        _units.Find(u => u.Id == id)
        ?? throw new KeyNotFoundException($"No unit {id} in the state.");

    /// <summary>The unit on the map at <paramref name="pos"/>, if any; downed soldiers still occupy their tile.</summary>
    public TacticalUnit? UnitAt(GridPos pos) => _units.Find(u => u.IsOnMap && u.Position == pos);

    public bool IsOccupied(GridPos pos) => UnitAt(pos) is not null;

    /// <summary>
    /// Walkable, unoccupied tiles nearest to <paramref name="anchor"/> (Euclidean; ties by Y then X
    /// so the order is deterministic), nearest first.
    /// </summary>
    public IEnumerable<GridPos> NearestFreeTiles(GridPos anchor)
    {
        var tiles = new List<GridPos>();
        for (var y = 0; y < Map.Height; y++)
        for (var x = 0; x < Map.Width; x++)
        {
            var pos = new GridPos(x, y);
            if (Map.IsWalkable(pos) && !IsOccupied(pos))
                tiles.Add(pos);
        }
        return tiles.OrderBy(p => Dist2(p, anchor)).ThenBy(p => p.Y).ThenBy(p => p.X);
    }

    private static int Dist2(GridPos a, GridPos b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>A cheap, deterministic hash of the whole state, for replay tests.</summary>
    public ulong StateHash() => StateHasher.Hash(this);
}
