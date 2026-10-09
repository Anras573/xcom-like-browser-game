using System.Numerics;
using Firewall.Web.Rendering;
using Yaeger.Platform;

namespace Firewall.Web.Ui;

/// <summary>Debug screen showing every widget in all of its states, plus live interactive ones.</summary>
public sealed class UiGalleryScene
{
    private static readonly string[] TabLabels = ["Barracks", "Research", "Workshop", "Map"];
    private static readonly string[] Icons =
    [
        "icon.target",
        "icon.warning",
        "icon.wrench",
        "icon.star",
        "icon.gear",
        "icon.locked",
    ];

    private int _tab;
    private bool _modalOpen;

    public bool ModalOpen => _modalOpen;
    private int _clicks;
    private string _lastModal = "-";

    public void Draw(UiContext ui)
    {
        var t = ui.Theme;
        var body = ui.Panel(new UiRect(16, 16, 620, 688), "UI gallery  (Esc to go back)");
        var x = body.X;
        var y = body.Y;

        ui.Label(
            new UiRect(x, y, 600, 22),
            "Buttons: normal / hover / pressed / disabled",
            t.Small
        );
        y += 24;
        var states = new[]
        {
            ButtonVisual.Normal,
            ButtonVisual.Hover,
            ButtonVisual.Pressed,
            ButtonVisual.Disabled,
        };
        for (var i = 0; i < states.Length; i++)
            ui.ButtonVisualAt(new UiRect(x + i * 148, y, 140, 40), states[i].ToString(), states[i]);
        y += 48;
        ui.ButtonVisualAt(
            new UiRect(x, y, 140, 40),
            "Primary",
            ButtonVisual.Normal,
            ButtonStyle.Primary
        );
        ui.ButtonVisualAt(
            new UiRect(x + 148, y, 140, 40),
            "Danger",
            ButtonVisual.Normal,
            ButtonStyle.Danger
        );
        if (
            ui.Button(
                new UiRect(x + 296, y, 296, 40),
                $"Live button  (clicks: {_clicks})",
                ButtonStyle.Primary,
                tooltip: "Click me.\n[c=#ffb000]Press and release inside[/c] to fire."
            )
        )
            _clicks++;
        y += 56;

        ui.Label(
            new UiRect(x, y, 600, 22),
            "Icon buttons: normal / hover / pressed / disabled / cooldown / selected",
            t.Small
        );
        y += 24;
        for (var i = 0; i < Icons.Length; i++)
        {
            var rect = new UiRect(x + i * 60, y, 52, 52);
            var visual = i switch
            {
                1 => ButtonVisual.Hover,
                2 => ButtonVisual.Pressed,
                3 => ButtonVisual.Disabled,
                _ => ButtonVisual.Normal,
            };
            ui.IconButtonVisualAt(
                rect,
                Icons[i],
                visual,
                (i + 1).ToString(),
                cooldown: i == 4 ? 2 : 0,
                selected: i == 5
            );
        }
        if (
            ui.IconButton(
                new UiRect(x + 6 * 60, y, 52, 52),
                "icon.exclamation",
                "7",
                "Live icon button\nHotkey [c=#3fd0ff]7[/c]"
            )
        )
            ui.Toast("Research complete: Drone Teardown");
        y += 66;

        ui.Label(new UiRect(x, y, 600, 22), "Progress bars and HP pips", t.Small);
        y += 24;
        ui.ProgressBar(new UiRect(x, y, 280, 22), 0.7f, 1f, BarColor.Green);
        ui.ProgressBar(new UiRect(x + 300, y, 280, 22), 0.25f, 1f, BarColor.Red);
        y += 30;
        ui.ProgressBar(new UiRect(x, y, 280, 22), 0.5f, 1f, BarColor.Blue);
        ui.ProgressBar(new UiRect(x + 300, y, 280, 22), 0.9f, 1f, BarColor.Yellow);
        y += 30;
        ui.ProgressBar(new UiRect(x, y, 280, 14), 7, 12, BarColor.Green, segments: 12);
        ui.ProgressBar(new UiRect(x + 300, y, 280, 14), 3, 12, BarColor.Red, segments: 12);
        y += 28;

        ui.Label(new UiRect(x, y, 600, 22), "Tabs", t.Small);
        y += 24;
        ui.Tabs(new UiRect(x, y, 592, 34), TabLabels, ref _tab);
        y += 42;
        ui.Label(
            new UiRect(x, y, 592, 22),
            $"Selected tab: {TabLabels[_tab]}",
            t.Body,
            TextAlignment.Center
        );
        y += 28;

        ui.Label(
            new UiRect(x, y, 592, 22),
            "Labels: [c=#6ee07a]rich[/c] text, centred and right-aligned",
            t.Body
        );
        y += 24;
        ui.Label(new UiRect(x, y, 592, 22), "Right aligned", t.Body, TextAlignment.Right);
        y += 26;
        if (ui.Button(new UiRect(x, y, 200, 36), "Open modal"))
            _modalOpen = true;
        ui.Button(
            new UiRect(x + 210, y, 200, 36),
            "Disabled",
            enabled: false,
            tooltip: "Unavailable\n[c=#ff4d4d]Not enough funds[/c]"
        );
        ui.Label(new UiRect(x + 420, y, 172, 36), $"Modal: {_lastModal}", t.Small);

        // Scroll list: 50 items, only visible ones are submitted.
        var list = ui.Panel(new UiRect(652, 16, 612, 688), "ScrollList (50 items)");
        var listRect = new UiRect(list.X, list.Y, list.W, list.H - 28);
        ui.ScrollList(
            "gallery",
            listRect,
            50,
            36,
            (i, r) =>
            {
                var row = r.Inset(0, 2);
                ui.Button(row with { W = row.W - 130 }, $"Item {i + 1:00}");
                ui.ProgressBar(
                    new UiRect(row.Right - 120, row.Y + 8, 114, 16),
                    (i * 7) % 12,
                    12,
                    BarColor.Green,
                    12
                );
            }
        );
        ui.Label(
            new UiRect(list.X, list.Bottom - 22, list.W, 22),
            $"items drawn: {ui.LastListItemsDrawn} / 50",
            t.Small
        );

        if (_modalOpen)
        {
            var r = ui.Modal(
                "Confirm recruit",
                "Recruit this operative for [c=#ffb000]§40[/c]?\nEnter confirms, Esc cancels.",
                ["Recruit", "Cancel"],
                0,
                1
            );
            if (r != UiContext.ModalPending)
            {
                _lastModal =
                    r == UiContext.ModalDismissed ? "dismissed"
                    : r == 0 ? "recruited"
                    : "cancelled";
                _modalOpen = false;
            }
        }
    }
}
