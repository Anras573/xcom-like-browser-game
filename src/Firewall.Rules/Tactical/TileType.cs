namespace Firewall.Rules.Tactical;

/// <summary>
/// Every terrain tile (GDD §4.1). The spawn glyphs <c>P</c>, <c>e</c> and <c>E</c> are not tile
/// types: they become <see cref="Floor"/> and are recorded on the <see cref="TacticalMap"/>.
/// </summary>
public enum TileType
{
    Floor,
    Road,
    Wall,
    Window,
    SmallCrate,
    BigCrate,
    Tree,
    Bush,
    Water,
    Doorway,
    Objective,
}

public enum CoverLevel
{
    None,
    Half,
    Full,
}
