using System;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace ForkedRoad;

internal sealed partial class ForkedRoadSpectatorOverlay : CanvasLayer
{
    private bool _built;
    private bool _isVisible;
    private bool _canGoLeft;
    private bool _canGoRight;

    private Control? _root;
    private Control? _contentHost;
    private Button? _leftButton;
    private Button? _rightButton;

    public event Action? RequestPrevious;

    public event Action? RequestNext;

    public override void _Ready()
    {
        EnsureBuilt();
        ApplyState();
        Log.Info($"ForkedRoad spectator overlay ready: layer={Layer} rootVisible={_root?.Visible} parent={GetParent()?.Name ?? "none"}");
    }

    public void SetViewContent(Control? node)
    {
        EnsureBuilt();
        if (_contentHost == null)
        {
            return;
        }

        foreach (Node child in _contentHost.GetChildren())
        {
            _contentHost.RemoveChild(child);
            child.Free();
        }

        if (node != null)
        {
            node.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            node.OffsetLeft = 0f;
            node.OffsetTop = 0f;
            node.OffsetRight = 0f;
            node.OffsetBottom = 0f;
            node.MouseFilter = Control.MouseFilterEnum.Ignore;
            _contentHost.AddChild(node);
            ApplyReadOnlyRecursive(node);
        }

        Log.Info($"ForkedRoad spectator overlay content updated: hasContent={node != null} contentChildren={_contentHost.GetChildCount()}");
    }

    public void UpdateState(bool isVisible, string statusText, string detailText, bool canGoLeft, bool canGoRight)
    {
        _isVisible = isVisible;
        _canGoLeft = canGoLeft;
        _canGoRight = canGoRight;
        EnsureBuilt();
        ApplyState();
    }

    private void EnsureBuilt()
    {
        if (_built)
        {
            return;
        }

        _built = true;
        Name = "ForkedRoadSpectatorOverlay";
        Layer = 100;

        _root = new Control
        {
            Name = "SpectatorRoot",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        _contentHost = new Control
        {
            Name = "SpectatorContentHost",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _contentHost.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_contentHost);

        _leftButton = new Button
        {
            Text = "<",
            CustomMinimumSize = new Vector2(84f, 180f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _leftButton.AnchorLeft = 0f;
        _leftButton.AnchorRight = 0f;
        _leftButton.AnchorTop = 0.5f;
        _leftButton.AnchorBottom = 0.5f;
        _leftButton.OffsetLeft = 24f;
        _leftButton.OffsetRight = 108f;
        _leftButton.OffsetTop = -90f;
        _leftButton.OffsetBottom = 90f;
        _leftButton.Pressed += () => RequestPrevious?.Invoke();
        _root.AddChild(_leftButton);

        _rightButton = new Button
        {
            Text = ">",
            CustomMinimumSize = new Vector2(84f, 180f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _rightButton.AnchorLeft = 1f;
        _rightButton.AnchorRight = 1f;
        _rightButton.AnchorTop = 0.5f;
        _rightButton.AnchorBottom = 0.5f;
        _rightButton.OffsetLeft = -108f;
        _rightButton.OffsetRight = -24f;
        _rightButton.OffsetTop = -90f;
        _rightButton.OffsetBottom = 90f;
        _rightButton.Pressed += () => RequestNext?.Invoke();
        _root.AddChild(_rightButton);
    }

    private void ApplyState()
    {
        if (_root == null)
        {
            return;
        }

        Visible = true;
        _root.Visible = _isVisible;

        if (_leftButton != null)
        {
            _leftButton.Disabled = !_canGoLeft;
        }

        if (_rightButton != null)
        {
            _rightButton.Disabled = !_canGoRight;
        }

        Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        Log.Info($"ForkedRoad spectator overlay apply: visible={_isVisible} canGoLeft={_canGoLeft} canGoRight={_canGoRight} viewport={viewportSize} rootVisible={_root.Visible}");
    }

    internal static void ApplyReadOnlyRecursive(Node node)
    {
        if (node is Control control)
        {
            control.MouseFilter = Control.MouseFilterEnum.Ignore;
            control.FocusMode = Control.FocusModeEnum.None;
        }

        if (node is BaseButton button)
        {
            button.Disabled = true;
        }

        if (node is NCombatRoom combatRoom)
        {
            combatRoom.MouseFilter = Control.MouseFilterEnum.Ignore;
            combatRoom.ProceedButton.Visible = false;
            foreach (NCreature creatureNode in combatRoom.CreatureNodes)
            {
                creatureNode.MouseFilter = Control.MouseFilterEnum.Ignore;
                creatureNode.Hitbox.MouseFilter = Control.MouseFilterEnum.Ignore;
            }
        }

        if (node is NTreasureRoom treasureRoom)
        {
            treasureRoom.MouseFilter = Control.MouseFilterEnum.Ignore;
            treasureRoom.ProceedButton.Visible = false;
        }

        foreach (Node child in node.GetChildren())
        {
            ApplyReadOnlyRecursive(child);
        }
    }
}

