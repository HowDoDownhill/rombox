using Godot;
using System.Linq;

// --setup-keys=a,b,up,down,left,right,stickup,stickdown sends those controller inputs in order. They
// are real joypad events, so a screen sees exactly what a controller sends. Each one logs what held
// focus when it was sent.
public static class ControllerTestKeys
{
    private const string ArgumentPrefix = "--setup-keys=";

    public static async void RunIfRequested(Node owner)
    {
        string keys = OS.GetCmdlineUserArgs().FirstOrDefault(argument => argument.StartsWith(ArgumentPrefix))?.Substring(ArgumentPrefix.Length);

        if (keys == null)
        {
            return;
        }

        SceneTree tree = owner.GetTree();
        await owner.ToSignal(tree.CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);

        foreach (string key in keys.Split(','))
        {
            if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree())
            {
                return;
            }

            Control focused = owner.GetViewport().GuiGetFocusOwner();
            GD.Print($"[Keys] {key} on {focused?.GetType().Name}:{(focused as Button)?.Text} editing={(focused as LineEdit)?.IsEditing()}");
            Input.ParseInputEvent(ControllerEvent(key, true));
            await owner.ToSignal(tree.CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            Input.ParseInputEvent(ControllerEvent(key, false));
            await owner.ToSignal(tree.CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        }
    }

    private static InputEvent ControllerEvent(string key, bool pressed)
    {
        if (key == "stickup" || key == "stickdown")
        {
            return new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = pressed ? (key == "stickup" ? -1.0f : 1.0f) : 0.0f };
        }

        JoyButton button = key switch
        {
            "a" => JoyButton.A,
            "b" => JoyButton.B,
            "up" => JoyButton.DpadUp,
            "down" => JoyButton.DpadDown,
            "left" => JoyButton.DpadLeft,
            _ => JoyButton.DpadRight
        };

        return new InputEventJoypadButton { ButtonIndex = button, Pressed = pressed };
    }
}
