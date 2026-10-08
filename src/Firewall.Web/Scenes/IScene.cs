namespace Firewall.Web.Scenes;

/// <summary>
/// One screen of the game (title, depot, tactical, a debug gallery, a pause overlay). A scene owns
/// its own Yaeger <c>World</c>, so leaving it is just destroying that world's entities.
/// </summary>
public interface IScene
{
    /// <summary>Name used in logs.</summary>
    string Name { get; }

    /// <summary>
    /// Overlays (pause menu, modal report) draw on top of the scene below it, which stays visible
    /// but frozen and receives no input. An overlay only has a screen pass: its own world is not drawn.
    /// </summary>
    bool IsOverlay { get; }

    /// <summary>Builds entities and loads what the scene needs.</summary>
    void Enter(SceneContext ctx);

    /// <summary>Only the top scene is updated; <paramref name="dt"/> is clamped to <see cref="SceneManager.MaxDeltaSeconds"/>.</summary>
    void Update(float dt);

    /// <summary>World pass + screen pass.</summary>
    void Render();

    /// <summary>Destroys this scene's entities.</summary>
    void Exit();

    /// <summary>Entities alive in the scene's world (logged on enter and exit to spot leaks).</summary>
    int EntityCount { get; }
}
