using Firewall.Web.Input;
using Firewall.Web.Scenes;
using Yaeger.ECS;

namespace Firewall.Web.Tests;

public class SceneManagerTests
{
    private sealed class FakeScene(string name, bool overlay = false, int entities = 3) : IScene
    {
        private readonly World _world = new();
        public string Name => name;
        public bool IsOverlay => overlay;
        public int Updates { get; private set; }
        public float LastDt { get; private set; }
        public int EntityCount => _world.Entities.Count();
        public bool Exited { get; private set; }
        public Action? OnUpdate { get; set; }

        public void Enter(SceneContext ctx)
        {
            for (var i = 0; i < entities; i++)
                _world.CreateEntity();
        }

        public void Update(float dt)
        {
            Updates++;
            LastDt = dt;
            OnUpdate?.Invoke();
        }

        public void Render() { }

        public void Exit()
        {
            Exited = true;
            foreach (var e in _world.Entities.ToList())
                _world.DestroyEntity(e);
        }
    }

    private readonly List<string> _log = [];
    private readonly SceneManager _scenes;

    public SceneManagerTests()
    {
        var input = new FakeInput();
        _scenes = new SceneManager(
            new SceneContext(input, new InputBindings(input), new ClickGate(input))
        )
        {
            Log = _log.Add,
        };
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds; t += 0.05f)
            _scenes.Update(0.05f);
    }

    [Fact]
    public void FirstPushIsImmediateAfterFlush()
    {
        var a = new FakeScene("a");
        _scenes.Push(a);
        _scenes.Flush();
        Assert.Same(a, _scenes.Top);
        Assert.False(_scenes.IsTransitioning);
    }

    [Fact]
    public void ReplaceFadesOutThenInAndOnlyTopUpdates()
    {
        var a = new FakeScene("a");
        var b = new FakeScene("b");
        _scenes.Push(a);
        _scenes.Flush();
        _scenes.Replace(b);

        _scenes.Update(0.05f); // starts the fade
        Assert.True(_scenes.IsTransitioning);
        _scenes.Update(0.1f);
        Assert.InRange(_scenes.FadeAlpha, 0.1f, 0.9f);
        Assert.Same(a, _scenes.Top); // still the old scene, frozen
        var updatesDuringFadeOut = a.Updates;

        Run(0.3f);
        Assert.Same(b, _scenes.Top);
        Assert.Equal(updatesDuringFadeOut, a.Updates);
        Run(0.5f);
        Assert.False(_scenes.IsTransitioning);
        Assert.Equal(0f, _scenes.FadeAlpha);
        Assert.True(b.Updates > 0);
    }

    [Fact]
    public void EntitiesDoNotLeakAcrossScenes()
    {
        var a = new FakeScene("a", entities: 5);
        var b = new FakeScene("b");
        _scenes.Push(a);
        _scenes.Flush();
        _scenes.Replace(b);
        Run(1f);

        Assert.True(a.Exited);
        Assert.Equal(0, a.EntityCount);
        Assert.Contains(_log, l => l.Contains("exit a: 5 -> 0"));
        Assert.Contains(_log, l => l.Contains("enter b: 3 entities"));
    }

    [Fact]
    public void OverlayFreezesSceneBelowAndPopsInstantly()
    {
        var a = new FakeScene("a");
        var pause = new FakeScene("pause", overlay: true);
        _scenes.Push(a);
        _scenes.Flush();

        _scenes.Push(pause);
        _scenes.Update(0.05f); // applied at the end of this update
        Assert.False(_scenes.IsTransitioning);
        Assert.Same(pause, _scenes.Top);
        Assert.Equal(2, _scenes.Stack.Count);

        var before = a.Updates;
        Run(0.5f);
        Assert.Equal(before, a.Updates);

        _scenes.Pop();
        _scenes.Update(0.05f);
        Assert.Same(a, _scenes.Top);
        Assert.False(_scenes.IsTransitioning);
    }

    [Fact]
    public void RequestFromUpdateIsAppliedAfterItSoOneKeyCannotCloseTwoScenes()
    {
        var a = new FakeScene("a");
        var pause = new FakeScene("pause", overlay: true);
        _scenes.Push(a);
        _scenes.Flush();
        _scenes.Push(pause);
        _scenes.Update(0.05f);

        pause.OnUpdate = () => _scenes.Pop();
        var before = a.Updates;
        _scenes.Update(0.05f);
        Assert.Same(a, _scenes.Top);
        Assert.Equal(before, a.Updates);
    }

    [Fact]
    public void DeltaIsClamped()
    {
        var a = new FakeScene("a");
        _scenes.Push(a);
        _scenes.Flush();
        _scenes.Update(30f); // a tab that was in the background for 30 s
        Assert.Equal(SceneManager.MaxDeltaSeconds, a.LastDt);
    }

    [Fact]
    public void DebugScenesReplaceByName()
    {
        DebugScenes.Register("t", () => new FakeScene("one"));
        DebugScenes.Register("t", () => new FakeScene("two"));
        Assert.Equal(1, DebugScenes.All.Count(e => e.Name == "t"));
        Assert.True(DebugScenes.TryCreate("t", out var s));
        Assert.Equal("two", s.Name);
    }
}
