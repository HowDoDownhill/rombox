using Godot;

// Lets a controller's A button use a screen that relies on Godot's own focus. A is the Select action,
// and Godot's ui_accept holds only keyboard keys, so these screens have to press the control themselves.
public static class ControllerPress
{
    public static bool IsPress(InputEvent inputEvent)
    {
        return inputEvent is InputEventJoypadButton && inputEvent.IsActionPressed("Select");
    }

    // Returns whether A did something on the focused control.
    public static bool Press(Control focused)
    {
        switch (focused)
        {
            case SettingsListEntry entry:
                entry.InteractWithWidget();
                return true;

            case LineEdit field:
                if (!field.IsEditing())
                {
                    field.Edit();
                }

                return true;

            case BaseButton button when !button.Disabled:
                button.EmitSignal(BaseButton.SignalName.Pressed);
                return true;

            default:
                return false;
        }
    }
}
