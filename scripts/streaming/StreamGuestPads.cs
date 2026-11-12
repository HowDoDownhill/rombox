using Godot;
using System;
using System.Linq;

// Reads the guest's controllers every frame and sends their state to the host. A state goes out
// when it changes and again every 100 ms, so a lost packet is corrected by the next one.
public sealed class StreamGuestPads
{
    private const long RepeatMs = 100;
    private const long UnplugRepeatMs = 1000;

    private sealed class Slot
    {
        public int DeviceId = -1;
        // True from the first state sent until the host has been told the controller is gone.
        public bool IsConnected;
        public bool SentConnected;
        public StreamPadState State;
        public long LastSentMs = -RepeatMs;
        public long UnpluggedAtMs;
    }

    private readonly StreamGuestSession session;
    private readonly Slot[] slots = Enumerable.Range(0, StreamProtocol.MaxPadsPerGuest).Select(_ => new Slot()).ToArray();

    // For tests on a machine with no controller: one made-up controller that presses A twice a
    // second and swings the left stick from side to side.
    public bool SendsTestPattern { get; set; }

    public StreamGuestPads(StreamGuestSession session)
    {
        this.session = session;
    }

    // hasFocus: input stops while the player is using another window.
    public void Update(bool hasFocus)
    {
        long now = (long)Time.GetTicksMsec();

        if (SendsTestPattern)
        {
            Send(slots[0], 0, true, TestPattern(now), now);
            return;
        }

        Godot.Collections.Array<int> connected = Input.GetConnectedJoypads();

        // A controller keeps its slot for as long as it stays plugged in, so unplugging one
        // never moves another player.
        foreach (int deviceId in connected)
        {
            if (!slots.Any(slot => slot.DeviceId == deviceId))
            {
                Slot free = slots.FirstOrDefault(slot => slot.DeviceId < 0 && !slot.IsConnected);

                if (free != null)
                {
                    free.DeviceId = deviceId;
                }
            }
        }

        for (int index = 0; index < slots.Length; index++)
        {
            Slot slot = slots[index];

            if (slot.DeviceId >= 0 && !connected.Contains(slot.DeviceId))
            {
                slot.DeviceId = -1;
                slot.UnpluggedAtMs = now;
            }

            if (slot.DeviceId >= 0)
            {
                Send(slot, index, true, hasFocus ? Read(slot.DeviceId) : default, now);
            }
            else if (slot.IsConnected)
            {
                // Said several times, because one lost packet would leave the controller plugged in on the host.
                Send(slot, index, false, default, now);
                slot.IsConnected = now - slot.UnpluggedAtMs < UnplugRepeatMs;
            }
        }
    }

    private void Send(Slot slot, int index, bool isConnected, StreamPadState state, long now)
    {
        if (isConnected == slot.SentConnected && state.Equals(slot.State) && now - slot.LastSentMs < RepeatMs)
        {
            return;
        }

        slot.IsConnected = isConnected || slot.IsConnected;
        slot.SentConnected = isConnected;
        slot.State = state;
        slot.LastSentMs = now;
        session.SendPadState(index, isConnected, state);
    }

    private static StreamPadState Read(int deviceId)
    {
        StreamPadButtons buttons = StreamPadButtons.None;

        void Add(JoyButton button, StreamPadButtons flag)
        {
            if (Input.IsJoyButtonPressed(deviceId, button))
            {
                buttons |= flag;
            }
        }

        // By position: Godot's A is the bottom face button on every controller.
        Add(JoyButton.A, StreamPadButtons.A);
        Add(JoyButton.B, StreamPadButtons.B);
        Add(JoyButton.X, StreamPadButtons.X);
        Add(JoyButton.Y, StreamPadButtons.Y);
        Add(JoyButton.LeftShoulder, StreamPadButtons.LeftShoulder);
        Add(JoyButton.RightShoulder, StreamPadButtons.RightShoulder);
        Add(JoyButton.Back, StreamPadButtons.Back);
        Add(JoyButton.Start, StreamPadButtons.Start);
        Add(JoyButton.Guide, StreamPadButtons.Guide);
        Add(JoyButton.LeftStick, StreamPadButtons.LeftStick);
        Add(JoyButton.RightStick, StreamPadButtons.RightStick);
        Add(JoyButton.DpadUp, StreamPadButtons.DpadUp);
        Add(JoyButton.DpadDown, StreamPadButtons.DpadDown);
        Add(JoyButton.DpadLeft, StreamPadButtons.DpadLeft);
        Add(JoyButton.DpadRight, StreamPadButtons.DpadRight);

        return new StreamPadState
        {
            Buttons = buttons,
            LeftTrigger = ToTrigger(Input.GetJoyAxis(deviceId, JoyAxis.TriggerLeft)),
            RightTrigger = ToTrigger(Input.GetJoyAxis(deviceId, JoyAxis.TriggerRight)),
            LeftX = ToStick(Input.GetJoyAxis(deviceId, JoyAxis.LeftX)),
            // Godot's vertical axes grow downward; an Xbox controller's grow upward.
            LeftY = ToStick(-Input.GetJoyAxis(deviceId, JoyAxis.LeftY)),
            RightX = ToStick(Input.GetJoyAxis(deviceId, JoyAxis.RightX)),
            RightY = ToStick(-Input.GetJoyAxis(deviceId, JoyAxis.RightY))
        };
    }

    private static StreamPadState TestPattern(long now)
    {
        return new StreamPadState
        {
            Buttons = now / 500 % 2 == 0 ? StreamPadButtons.A : StreamPadButtons.None,
            LeftX = ToStick((float)Math.Sin(now / 1000.0 * Math.PI))
        };
    }

    private static short ToStick(float value)
    {
        return (short)Math.Clamp(Math.Round(value * 32767.0), -32767, 32767);
    }

    private static byte ToTrigger(float value)
    {
        return (byte)Math.Clamp(Math.Round(value * 255.0), 0, 255);
    }
}
