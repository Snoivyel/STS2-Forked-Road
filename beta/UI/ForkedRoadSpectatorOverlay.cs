using System;
using Godot;

namespace ForkedRoad;

internal sealed partial class ForkedRoadSpectatorOverlay : PanelContainer
{
    private Label? _statusLabel;
    private Label? _detailLabel;
    private Button? _leftButton;
    private Button? _rightButton;

    public event Action? RequestPrevious;

    public event Action? RequestNext;

    public override void _Ready()
    {
        Name = "ForkedRoadSpectatorOverlay";
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorTop = 0f;
        AnchorBottom = 0f;
        OffsetLeft = -280f;
        OffsetRight = 280f;
        OffsetTop = 32f;
        OffsetBottom = 220f;
        Visible = false;
        Modulate = new Color(1f, 1f, 1f, 0.96f);

        VBoxContainer root = new();
        Label title = new()
        {
            Text = "ForkedRoad Spectator",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        root.AddChild(title);

        _statusLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        root.AddChild(_statusLabel);

        _detailLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Top,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        root.AddChild(_detailLabel);

        HBoxContainer buttons = new()
        {
            Alignment = BoxContainer.AlignmentMode.Center
        };

        _leftButton = new Button { Text = "< Prev" };
        _leftButton.Pressed += () => RequestPrevious?.Invoke();
        buttons.AddChild(_leftButton);

        _rightButton = new Button { Text = "Next >" };
        _rightButton.Pressed += () => RequestNext?.Invoke();
        buttons.AddChild(_rightButton);

        root.AddChild(buttons);
        AddChild(root);
    }

    public void UpdateState(bool isVisible, string statusText, string detailText, bool canGoLeft, bool canGoRight)
    {
        Visible = isVisible;
        if (_statusLabel != null)
        {
            _statusLabel.Text = statusText;
        }
        if (_detailLabel != null)
        {
            _detailLabel.Text = detailText;
        }
        if (_leftButton != null)
        {
            _leftButton.Disabled = !canGoLeft;
        }
        if (_rightButton != null)
        {
            _rightButton.Disabled = !canGoRight;
        }
    }
}
