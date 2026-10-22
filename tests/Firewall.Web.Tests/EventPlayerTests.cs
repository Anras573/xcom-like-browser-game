using System.Numerics;
using Firewall.Rules;
using Firewall.Rules.Tactical;
using Firewall.Web.Playback;
using Firewall.Web.Rendering;

namespace Firewall.Web.Tests;

public class EventPlayerTests
{
    private sealed class FakeAnimation(string name, float duration, List<string> log)
        : IEventAnimation
    {
        private float _elapsed;
        public float Received { get; private set; }

        public void Start(ViewModel vm, EventPlayerContext ctx) => log.Add($"start {name}");

        public bool Update(float dt)
        {
            Received += dt;
            _elapsed += dt;
            if (_elapsed < duration)
                return false;
            log.Add($"end {name}");
            return true;
        }
    }

    private static (
        EventPlayer Player,
        List<string> Log,
        Dictionary<string, FakeAnimation> Made
    ) Make(params (GameEvent Event, string Name, float Duration)[] script)
    {
        var log = new List<string>();
        var made = new Dictionary<string, FakeAnimation>();
        var byEvent = script.ToDictionary(s => s.Event, s => s);
        var player = new EventPlayer(
            new ViewModel(),
            new EventPlayerContext(),
            e =>
            {
                var (_, name, duration) = byEvent[e];
                return made[name] = new FakeAnimation(name, duration, log);
            }
        );
        player.Enqueue(script.Select(s => s.Event).ToList());
        return (player, log, made);
    }

    private static GameEvent Ev(int n) => new ApChanged(new UnitId(n), n);

    [Fact]
    public void PlaysEventsInOrderAndStaysBusyUntilTheLastEnds()
    {
        var (a, b, c) = (Ev(1), Ev(2), Ev(3));
        var (player, log, _) = Make((a, "a", 0.3f), (b, "b", 0.2f), (c, "c", 0.2f));

        Assert.True(player.IsBusy);
        player.Update(0.1f);
        Assert.Equal(["start a"], log);
        player.Update(0.25f); // a ends, b starts
        Assert.Equal(["start a", "end a", "start b"], log);
        Assert.True(player.IsBusy);
        player.Update(0.25f);
        player.Update(0.1f);
        Assert.True(player.IsBusy);
        player.Update(0.2f);

        Assert.False(player.IsBusy);
        Assert.Equal(["start a", "end a", "start b", "end b", "start c", "end c"], log);
    }

    [Fact]
    public void InstantAnimationsDoNotCostAFrame()
    {
        var (a, b, c) = (Ev(1), Ev(2), Ev(3));
        var (player, log, _) = Make((a, "a", 0f), (b, "b", 0f), (c, "c", 0.5f));

        player.Update(0.016f);

        Assert.Equal(["start a", "end a", "start b", "end b", "start c"], log);
        Assert.True(player.IsBusy);
    }

    [Fact]
    public void FinishingAnimationsPassNoTimeOnToTheNext()
    {
        var (a, b) = (Ev(1), Ev(2));
        var (player, _, made) = Make((a, "a", 0.1f), (b, "b", 1f));

        player.Update(0.5f);

        Assert.Equal(0.5f, made["a"].Received, 3);
        Assert.Equal(0f, made["b"].Received, 3);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    [InlineData(4f)]
    public void SpeedScalesTheTimeAnimationsSee(float speed)
    {
        var a = Ev(1);
        var (player, _, made) = Make((a, "a", 10f));
        player.Speed = speed;

        player.Update(0.1f);

        Assert.Equal(0.1f * speed, made["a"].Received, 4);
    }

    [Fact]
    public void FasterSpeedFinishesSooner()
    {
        int FramesAt(float speed)
        {
            var a = Ev(1);
            var (player, _, _) = Make((a, "a", 1f));
            player.Speed = speed;
            var frames = 0;
            while (player.IsBusy && frames < 10_000)
            {
                player.Update(0.01f);
                frames++;
            }
            return frames;
        }

        Assert.InRange(FramesAt(1f), 99, 101);
        Assert.True(FramesAt(2f) < 55);
        Assert.True(FramesAt(4f) < 30);
    }

    [Fact]
    public void EventsWithoutAnAnimationAreSkipped()
    {
        var player = new EventPlayer(new ViewModel(), new EventPlayerContext(), _ => null);
        player.Enqueue([Ev(1), Ev(2)]);

        player.Update(0.016f);

        Assert.False(player.IsBusy);
    }

    [Fact]
    public void ViewModelEffectsAgeAtPlaybackSpeed()
    {
        var vm = new ViewModel();
        vm.AddFloater("x", Yaeger.Graphics.Color.White, 20, Vector2.Zero);
        var player = new EventPlayer(vm, new EventPlayerContext(), _ => null) { Speed = 4f };

        player.Update(0.1f);

        Assert.Equal(0.4f, vm.Floaters[0].Age, 3);
    }
}

public class PlaybackSettingsTests
{
    [Fact]
    public void CyclesThroughThreeSpeedsAndWraps()
    {
        var settings = new PlaybackSettings();

        Assert.Equal(1f, settings.Speed);
        settings.Cycle();
        Assert.Equal(1.5f, settings.Speed);
        settings.Cycle();
        Assert.Equal(2f, settings.Speed);
        settings.Cycle();
        Assert.Equal(1f, settings.Speed);
    }

    [Fact]
    public void HoldingFastForwardPlaysAtFourTimes()
    {
        var settings = new PlaybackSettings();
        settings.Cycle();

        Assert.Equal(1.5f, settings.Effective(false));
        Assert.Equal(4f, settings.Effective(true));
        Assert.Equal("1.5x", settings.Label);
    }
}

public class CameraDirectorTests
{
    [Fact]
    public void PointsNearTheMiddleAreWellInside()
    {
        var center = new Vector2(10f, 10f);

        Assert.True(
            CameraDirector.IsWellInside(center, 11f, 16f / 9f, new Vector2(10f, 10f), 0.7f)
        );
        Assert.True(
            CameraDirector.IsWellInside(center, 11f, 16f / 9f, new Vector2(13f, 12f), 0.7f)
        );
    }

    [Fact]
    public void PointsNearTheEdgeOrOffScreenAreNot()
    {
        var center = new Vector2(10f, 10f);

        // 11 tiles tall: the view spans +-5.5, the inner area +-3.85.
        Assert.False(
            CameraDirector.IsWellInside(center, 11f, 16f / 9f, new Vector2(10f, 14.5f), 0.7f)
        );
        Assert.False(
            CameraDirector.IsWellInside(center, 11f, 16f / 9f, new Vector2(30f, 10f), 0.7f)
        );
    }
}

public class FloaterTests
{
    private static readonly Yaeger.Graphics.Color White = Yaeger.Graphics.Color.White;

    [Fact]
    public void TextsOnTheSameSpotStackUpward()
    {
        var vm = new ViewModel();
        var a = vm.AddFloater("-4", White, 22, new Vector2(3.5f, 3.5f));
        var b = vm.AddFloater("-1 ARMOR", White, 18, new Vector2(3.5f, 3.5f));
        var elsewhere = vm.AddFloater("MISSED", White, 22, new Vector2(8.5f, 3.5f));

        Assert.Equal(0, a.StackSlot);
        Assert.Equal(1, b.StackSlot);
        Assert.Equal(0, elsewhere.StackSlot);
        Assert.Equal(FloaterVm.StackGap, b.Position.Y - a.Position.Y, 3);
    }

    [Fact]
    public void RisesSixTenthsOfATileAndFadesOverNinetyHundredths()
    {
        var vm = new ViewModel();
        var f = vm.AddFloater("-4", White, 22, Vector2.Zero);

        Assert.Equal(1f, f.Alpha);
        vm.Update(0.45f);
        Assert.Equal(0.3f, f.Position.Y, 3);
        Assert.Equal(1f, f.Alpha, 3);
        vm.Update(0.2f);
        Assert.True(f.Alpha < 1f);
        vm.Update(0.3f);

        Assert.Empty(vm.Floaters);
    }

    [Fact]
    public void ASpotFreesUpOnceItsTextsAreGone()
    {
        var vm = new ViewModel();
        vm.AddFloater("-4", White, 22, Vector2.Zero);
        vm.Update(1f);

        Assert.Equal(0, vm.AddFloater("-2", White, 22, Vector2.Zero).StackSlot);
    }
}

public class MoveEasingTests
{
    [Fact]
    public void EasingMakesTheWalkSlowAtBothEndsWithTheSameDuration()
    {
        GridPos[] path = [new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(4, 0)];
        var plain = new MoveTween(path);
        var eased = new MoveTween(path, ease: true);

        Assert.Equal(plain.Duration, eased.Duration, 4);
        // A quarter of the way in, the eased walker has covered less than a quarter.
        Assert.True(
            eased.Position(eased.Duration / 4f, out _).X
                < plain.Position(plain.Duration / 4f, out _).X
        );
        Assert.Equal(2.5f, eased.Position(eased.Duration / 2f, out _).X, 3);
        Assert.Equal(4.5f, eased.Position(eased.Duration, out _).X, 3);
    }

    [Fact]
    public void DashesAreFasterThanWalks()
    {
        GridPos[] path = [new(0, 0), new(1, 0), new(2, 0)];

        Assert.Equal(0.18f, new MoveTween(path, MoveTween.DashSecondsPerTile).Duration, 3);
        Assert.Equal(0.24f, new MoveTween(path).Duration, 3);
    }
}
