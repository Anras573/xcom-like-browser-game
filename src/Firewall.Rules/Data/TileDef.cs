using System.Text.Json.Serialization;
using Firewall.Rules.Tactical;

namespace Firewall.Rules.Data;

/// <summary>Properties of one <see cref="TileType"/>, from <c>data/tiles.json</c>.</summary>
public sealed record TileDef : IDef
{
    [JsonRequired]
    public TileType Type { get; init; }

    /// <summary>The character used for this tile in map files.</summary>
    [JsonRequired]
    public string Glyph { get; init; } = "";

    [JsonRequired]
    public bool Walkable { get; init; }

    public CoverLevel Cover { get; init; }

    public bool BlocksLos { get; init; }

    /// <summary>What the tile turns into when destroyed; null when it cannot be destroyed.</summary>
    public TileType? DestructibleInto { get; init; }

    /// <summary>Hits needed to destroy it; 0 when indestructible.</summary>
    public int HitsToDestroy { get; init; }

    [JsonIgnore]
    public string Id => Type.ToString();
}
