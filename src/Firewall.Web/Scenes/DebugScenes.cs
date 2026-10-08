namespace Firewall.Web.Scenes;

/// <summary>
/// Registry behind the debug menu. Later issues hang their test scenes on it:
/// <c>DebugScenes.Register("name", () =&gt; new XScene())</c>.
/// </summary>
public static class DebugScenes
{
    private static readonly List<(string Name, Func<IScene> Create)> Entries = [];

    public static IReadOnlyList<(string Name, Func<IScene> Create)> All => Entries;

    /// <summary>Adds a scene, or replaces the factory if the name is already registered.</summary>
    public static void Register(string name, Func<IScene> create)
    {
        var i = Entries.FindIndex(e => e.Name == name);
        if (i >= 0)
            Entries[i] = (name, create);
        else
            Entries.Add((name, create));
    }

    public static bool TryCreate(string name, out IScene scene)
    {
        var entry = Entries.Find(e => e.Name == name);
        scene = entry.Create?.Invoke()!;
        return scene is not null;
    }

    /// <summary>The scenes that ship with the foundation epic.</summary>
    public static void RegisterBuiltIn()
    {
        Register("Sprite gallery", () => new SpriteGalleryScene());
        Register("UI gallery", () => new UiGalleryDebugScene());
        Register("Text test", () => new TextTestScene());
        Register("Picking test", () => new PickingTestScene());
    }
}
