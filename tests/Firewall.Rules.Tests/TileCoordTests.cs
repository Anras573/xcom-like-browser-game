using Firewall.Rules;

namespace Firewall.Rules.Tests;

public class TileCoordTests
{
    [Fact]
    public void DistanceTo_UsesEuclideanDistance()
    {
        Assert.Equal(5.0, new TileCoord(0, 0).DistanceTo(new TileCoord(3, 4)), precision: 6);
    }
}
