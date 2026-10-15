using Firewall.Rules;

namespace Firewall.Rules.Tests;

public class GridPosTests
{
    [Fact]
    public void DistanceTo_UsesEuclideanDistance()
    {
        Assert.Equal(5.0, new GridPos(0, 0).DistanceTo(new GridPos(3, 4)), precision: 6);
    }
}
