namespace Firewall.Web.Playback;

/// <summary>
/// How fast combat plays back: a chosen speed, and a faster one while the fast-forward key is held.
/// Neither changes outcomes, only how quickly animations run.
/// </summary>
public sealed class PlaybackSettings
{
    public static readonly IReadOnlyList<float> Speeds = [1f, 1.5f, 2f];

    /// <summary>Speed while <c>Space</c> is held.</summary>
    public const float HoldSpeed = 4f;

    public float Speed { get; private set; } = Speeds[0];

    /// <summary>Steps to the next entry of <see cref="Speeds"/>, wrapping around.</summary>
    public void Cycle()
    {
        var i = Speeds.ToList().IndexOf(Speed);
        Speed = Speeds[(i + 1) % Speeds.Count];
    }

    public float Effective(bool fastForwardHeld) => fastForwardHeld ? HoldSpeed : Speed;

    public string Label => $"{Speed:0.#}x";
}
