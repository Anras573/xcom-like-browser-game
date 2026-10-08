using System.Numerics;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Yaeger.Graphics;

namespace Firewall.Web.Scenes;

/// <summary>One unit of loading work; <c>Run</c> reports its own 0..1 progress.</summary>
public sealed record BootStep(string Label, Func<IProgress<float>, Task> Run);

/// <summary>Runs the loading steps in order behind a progress bar, then moves to the title.</summary>
public sealed class BootScene(IReadOnlyList<BootStep> steps, Func<IScene> next) : SceneBase
{
    private readonly object _lock = new();
    private int _index;
    private float _stepProgress;
    private Task? _running;
    private string? _error;

    public override string Name => "Boot";

    /// <summary>0..1 across all steps.</summary>
    public float Progress =>
        steps.Count == 0 ? 1f : Math.Clamp((_index + _stepProgress) / steps.Count, 0f, 1f);

    public bool Failed => _error is not null;

    protected override void OnUpdate(float dt)
    {
        if (_error is not null)
            return;

        if (_running is { IsCompleted: true } done)
        {
            if (done.IsFaulted)
            {
                _error = done.Exception?.GetBaseException().Message ?? "load failed";
                return;
            }
            _running = null;
            _index++;
            _stepProgress = 0f;
        }

        if (_running is null)
        {
            if (_index >= steps.Count)
            {
                Ctx.Scenes.Replace(next());
                return;
            }
            _running = steps[_index].Run(new Reporter(this));
        }
    }

    private sealed class Reporter(BootScene scene) : IProgress<float>
    {
        public void Report(float value) => scene._stepProgress = Math.Clamp(value, 0f, 1f);
    }

    // Fonts are the first step, so only draw text once it is done.
    protected override void DrawCanvas(ScreenCanvas canvas)
    {
        var barW = 480f;
        var x = (UiSpace.LogicalWidth - barW) / 2f;
        var y = UiSpace.LogicalHeight / 2f;
        canvas.FillRect(
            new Vector2(x - 2, y - 2),
            new Vector2(barW + 4, 20),
            new Vector4(1, 1, 1, 0.25f)
        );
        canvas.FillRect(
            new Vector2(x, y),
            new Vector2(barW * Progress, 16),
            _error is null ? new Vector4(0.25f, 0.8f, 1f, 1f) : new Vector4(1f, 0.3f, 0.3f, 1f)
        );
        if (_index == 0 && _error is null)
            return;
        var label = _error is null
            ? (_index < steps.Count ? steps[_index].Label : "Ready")
            : $"Load failed: {_error}";
        canvas.Text(label, new Vector2(x, y + 28), TextStyles.Small);
    }
}
