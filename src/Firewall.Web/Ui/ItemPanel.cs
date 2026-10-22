using Firewall.Rules.Tactical;
using Firewall.Web.Rendering;

namespace Firewall.Web.Ui;

/// <summary>The text behind the targeting panel of a thrown item: who it affects, the damage, the cover it breaks.</summary>
public static class ItemPanelText
{
    /// <summary>"Affects: 2 enemies, 1 ally", or "Affects: nobody".</summary>
    public static string Affects(BlastPreview preview)
    {
        var parts = new List<string>();
        if (preview.Enemies > 0)
            parts.Add(preview.Enemies == 1 ? "1 enemy" : $"{preview.Enemies} enemies");
        if (preview.Allies > 0)
            parts.Add(preview.Allies == 1 ? "1 ally" : $"{preview.Allies} allies");
        return $"Affects: {(parts.Count == 0 ? "nobody" : string.Join(", ", parts))}";
    }

    /// <summary>"Cover destroyed: 2", or null when nothing breaks.</summary>
    public static string? CoverDestroyed(BlastPreview preview) =>
        preview.Breakage.Count == 0 ? null : $"Cover destroyed: {preview.Breakage.Count}";

    /// <summary>The lines of the panel body, top to bottom.</summary>
    public static IReadOnlyList<string> Lines(BlastPreview preview)
    {
        var lines = new List<string> { Affects(preview) };
        if (preview.Damage > 0)
            lines.Add($"Damage: {preview.Damage}");
        if (CoverDestroyed(preview) is { } cover)
            lines.Add(cover);
        var kills = preview.Victims.Count(v => !v.Friendly && v.Kills);
        if (kills > 0)
            lines.Add(kills == 1 ? "Would kill 1 enemy" : $"Would kill {kills} enemies");
        var friendlyFire = preview.Victims.Count(v => v.Friendly);
        if (friendlyFire > 0 && preview.Damage > 0)
            lines.Add(
                friendlyFire == 1
                    ? "Friendly fire: 1 ally"
                    : $"Friendly fire: {friendlyFire} allies"
            );
        return lines;
    }
}

/// <summary>The bottom-centre panel while a thrown item is aimed.</summary>
public static class ItemPanel
{
    public const float Width = 480f;

    public static void Draw(
        UiContext ui,
        string title,
        BlastPreview? preview,
        string hint,
        float bottomMargin
    )
    {
        var lines = preview is null ? ["Aim at a highlighted tile."] : ItemPanelText.Lines(preview);
        var height = 56f + lines.Count * 22f + 26f;
        var rect = new UiRect(
            (UiSpace.LogicalWidth - Width) / 2f,
            UiSpace.LogicalHeight - height - bottomMargin,
            Width,
            height
        );
        var content = ui.Panel(rect, title);
        for (var i = 0; i < lines.Count; i++)
            ui.Label(
                new UiRect(content.X, content.Y + 4f + i * 22f, content.W, 22f),
                lines[i],
                ui.Theme.Body
            );
        ui.Label(new UiRect(content.X, rect.Bottom - 34f, content.W, 22f), hint, ui.Theme.Small);
    }
}
