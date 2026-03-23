using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ForkedRoad;

public partial class ForkedRoadWaitingRoom : Control
{
    public static ForkedRoadWaitingRoom Create(IEnumerable<string> activePlayers, int branchSequence, string? branchLabel = null)
    {
        ForkedRoadWaitingRoom room = new ForkedRoadWaitingRoom
        {
            Name = "ForkedRoadWaitingRoom"
        };
        room.SetAnchorsPreset(LayoutPreset.FullRect);

        ColorRect backdrop = new ColorRect
        {
            Color = new Color(0.03f, 0.04f, 0.06f, 0.96f)
        };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        room.AddChild(backdrop);

        CenterContainer center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        room.AddChild(center);

        PanelContainer panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(720f, 180f);
        center.AddChild(panel);

        VBoxContainer content = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        content.AddThemeConstantOverride("separation", 12);
        panel.AddChild(content);

        Label header = new Label
        {
            Text = $"Forked Road\nResolving branch {branchSequence}",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.AddThemeFontSizeOverride("font_size", 28);
        content.AddChild(header);

        if (!string.IsNullOrWhiteSpace(branchLabel))
        {
            Label branchType = new Label
            {
                Text = branchLabel,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            branchType.AddThemeFontSizeOverride("font_size", 22);
            content.AddChild(branchType);
        }

        Label details = new Label
        {
            Text = "Active players: " + string.Join(", ", activePlayers.Where(static p => !string.IsNullOrWhiteSpace(p))),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        details.AddThemeFontSizeOverride("font_size", 20);
        content.AddChild(details);

        return room;
    }
}
