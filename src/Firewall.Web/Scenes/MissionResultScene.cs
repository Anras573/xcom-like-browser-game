using System.Numerics;
using Firewall.Rules.Data;
using Firewall.Rules.Tactical;
using Firewall.Web.Input;
using Firewall.Web.Rendering;
using Firewall.Web.Ui;
using Yaeger.Graphics;
using Yaeger.Platform;

namespace Firewall.Web.Scenes;

/// <summary>The pure text behind <see cref="MissionResultScene"/>.</summary>
public static class MissionResultText
{
    public static string Title(MissionOutcome outcome) =>
        outcome switch
        {
            MissionOutcome.Success => "MISSION SUCCESS",
            MissionOutcome.Failure => "MISSION FAILED",
            _ => "MISSION ABORTED",
        };

    /// <summary>"Healthy", "Wounded (-3 HP)", "KIA" or "MIA".</summary>
    public static string Fate(SoldierResult soldier) =>
        soldier.Status switch
        {
            SoldierFate.Healthy => "Healthy",
            SoldierFate.Wounded => $"Wounded (-{soldier.HpLost} HP)",
            SoldierFate.Dead => "KIA",
            _ => "MIA",
        };

    /// <summary>"2x Warden, 1x Enforcer" in kill-table order; "None" when nothing died.</summary>
    public static string EnemiesKilled(MissionResult result, GameData data) =>
        result.EnemiesKilled.Count == 0
            ? "None"
            : string.Join(
                ", ",
                result.EnemiesKilled.Select(kv =>
                    $"{kv.Value}x {(data.Enemies.TryGet(kv.Key, out var def) ? def.Name : kv.Key)}"
                )
            );

    /// <summary>"8 Salvage, 20 Credits, 1 Core"; "Nothing" when empty.</summary>
    public static string Loot(Loot loot)
    {
        var parts = new List<string>();
        if (loot.Salvage > 0)
            parts.Add($"{loot.Salvage} Salvage");
        if (loot.Credits > 0)
            parts.Add($"{loot.Credits} Credits");
        if (loot.Cores > 0)
            parts.Add(loot.Cores == 1 ? "1 Core" : $"{loot.Cores} Cores");
        return parts.Count == 0 ? "Nothing" : string.Join(", ", parts);
    }
}

/// <summary>
/// Temporary mission result screen (the debrief replaces it in #41): outcome, soldier fates,
/// kills and loot, and a way back to the title.
/// </summary>
public sealed class MissionResultScene(MissionResult result) : SceneBase
{
    private static readonly string[] Columns = ["Soldier", "Status", "Kills", "Damage", "Hits"];
    private static readonly float[] ColumnX = [0f, 290f, 510f, 600f, 700f];

    public override string Name => "MissionResult";

    public MissionResult Result => result;

    protected override void OnUpdate(float dt)
    {
        if (
            Ctx.Bindings.WasPressed(GameAction.Cancel)
            || Ctx.Bindings.WasPressed(GameAction.Confirm)
        )
            BackToTitle();
    }

    // Pops back down to the title, wherever the skirmish was opened from.
    private void BackToTitle()
    {
        var stack = Ctx.Scenes.Stack;
        var title = stack.ToList().FindLastIndex(s => s is TitleScene);
        if (title < 0)
        {
            Ctx.Scenes.Replace(new TitleScene());
            return;
        }
        for (var i = stack.Count - 1; i > title; i--)
            Ctx.Scenes.Pop();
    }

    protected override void DrawScreen(ScreenCanvas canvas, UiContext ui)
    {
        canvas.FillRect(
            ui.Space.CanvasRect.Position,
            ui.Space.CanvasRect.Size,
            new Vector4(0.03f, 0.04f, 0.06f, 1f)
        );
        const float w = 820f;
        const float h = 560f;
        var body = ui.Panel(
            new UiRect((UiSpace.LogicalWidth - w) / 2f, (UiSpace.LogicalHeight - h) / 2f, w, h),
            "Mission Result"
        );

        var titleColor = result.Outcome switch
        {
            MissionOutcome.Success => new Color(110, 224, 122),
            MissionOutcome.Failure => new Color(255, 90, 80),
            _ => new Color(255, 190, 70),
        };
        var title = MissionResultText.Title(result.Outcome);
        canvas.Text(title, new Vector2(body.X, body.Y), TextStyles.Heading.With(titleColor));
        canvas.Text(
            $"Turns taken: {result.TurnsTaken}",
            new Vector2(body.X, body.Y + 40f),
            TextStyles.Body
        );

        var y = body.Y + 84f;
        for (var i = 0; i < Columns.Length; i++)
            canvas.Text(Columns[i], new Vector2(body.X + ColumnX[i], y), TextStyles.Small);
        y += 24f;
        foreach (var s in result.Soldiers)
        {
            var name = s.Unit == result.Mvp ? $"{s.Name}  (MVP)" : s.Name;
            var style = s.Survived
                ? TextStyles.Body
                : TextStyles.Body.With(new Color(235, 110, 100));
            string[] cells =
            [
                name,
                MissionResultText.Fate(s),
                s.Kills.ToString(),
                s.DamageDealt.ToString(),
                $"{s.Hits}/{s.ShotsFired}",
            ];
            for (var i = 0; i < cells.Length; i++)
                canvas.Text(cells[i], new Vector2(body.X + ColumnX[i], y), style);
            y += 28f;
        }

        y += 20f;
        canvas.Text(
            $"Enemies killed: {MissionResultText.EnemiesKilled(result, Ctx.Data)}",
            new Vector2(body.X, y),
            TextStyles.Body
        );
        canvas.Text(
            $"Loot recovered: {MissionResultText.Loot(result.LootCollected)}",
            new Vector2(body.X, y + 30f),
            TextStyles.Body
        );
        if (result.Outcome != MissionOutcome.Success)
            canvas.Text(
                "Loot is only recovered when the mission succeeds.",
                new Vector2(body.X, y + 56f),
                TextStyles.Small
            );

        if (
            ui.Button(
                new UiRect(body.Right - 220f, body.Bottom - 44f, 220f, 44f),
                "Back to Title",
                ButtonStyle.Primary
            )
        )
            BackToTitle();
    }
}
