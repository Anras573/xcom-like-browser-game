using Firewall.Rules.Data;
using Firewall.Rules.Tactical;
using Firewall.Web.Scenes;

namespace Firewall.Web.Tests;

public class SkirmishMenuTests
{
    [Fact]
    public void CyclingWrapsAroundEveryChoice()
    {
        var model = new SkirmishMenuModel();
        model.CycleType(-1);
        Assert.Equal(MissionType.Capture, model.Type);
        model.CycleType(1);
        Assert.Equal(MissionType.Purge, model.Type);

        model.CycleBiome(-1);
        Assert.Equal(Biome.Suburban, model.Biome);

        model.CycleDifficulty(-1);
        Assert.Equal(Difficulty.Recruit, model.Difficulty);
    }

    [Fact]
    public void ForceLevelStaysBetweenOneAndTen()
    {
        var model = new SkirmishMenuModel();
        for (var i = 0; i < 20; i++)
        {
            model.CycleForceLevel(1);
            Assert.InRange(model.ForceLevel, 1, 10);
        }
        model.CycleForceLevel(-model.ForceLevel);
        Assert.Equal(10, model.ForceLevel);
    }

    [Fact]
    public void TheSpecUsesTheFixedSeedOrTheRandomOne()
    {
        var model = new SkirmishMenuModel();
        model.CycleType(1);
        Assert.Equal(7UL, model.ToSpec(7).Seed);
        model.Seed = 42;
        var spec = model.ToSpec(7);
        Assert.Equal(42UL, spec.Seed);
        Assert.Equal(MissionType.Rescue, spec.Type);
        Assert.Equal("rescue", spec.MissionId);
    }
}
