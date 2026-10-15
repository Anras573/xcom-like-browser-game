using Firewall.Rules.Rng;

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
/// Everything a tactical mission is made of: map, units, turn, RNG and objective. Visibility
/// (fog of war) joins it with the line-of-sight work.
/// </summary>
public sealed class TacticalState(TacticalMap map, IRandom rng, string missionId = "purge")
{
    private readonly List<TacticalUnit> _units = [];
    private readonly List<Pod> _pods = [];
    private int _nextUnitId = 1;
    private int _nextPodId = 1;

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

    /// <summary>The living unit standing on <paramref name="pos"/>, if any.</summary>
    public TacticalUnit? UnitAt(GridPos pos) => _units.Find(u => u.IsAlive && u.Position == pos);

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
