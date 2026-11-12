using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

// Linux: virtual Xbox 360 controllers through /dev/uinput, the kernel's own interface for
// user-made input devices. The device reports the same name, ids, buttons and axes as the kernel's
// xpad driver gives a wired Xbox 360 controller, so SDL and emulators map it the same way.
public sealed class UinputPadBackend : IStreamVirtualPadBackend
{
    private const string DevicePath = "/dev/uinput";
    private const int OpenWriteOnly = 0x1;
    private const int OpenNonBlocking = 0x800;

    private const uint SetEventBit = 0x40045564;
    private const uint SetKeyBit = 0x40045565;
    private const uint SetAbsoluteBit = 0x40045567;
    private const uint DeviceSetup = 0x405C5503;
    private const uint AbsoluteSetup = 0x401C5504;
    private const uint DeviceCreate = 0x5501;
    private const uint DeviceDestroy = 0x5502;

    private const ushort EventSync = 0x00;
    private const ushort EventKey = 0x01;
    private const ushort EventAbsolute = 0x03;

    private const ushort AxisLeftX = 0x00;
    private const ushort AxisLeftY = 0x01;
    private const ushort AxisLeftTrigger = 0x02;
    private const ushort AxisRightX = 0x03;
    private const ushort AxisRightY = 0x04;
    private const ushort AxisRightTrigger = 0x05;
    private const ushort AxisDpadX = 0x10;
    private const ushort AxisDpadY = 0x11;

    private const ushort BusUsb = 0x03;
    private const ushort MicrosoftVendorId = 0x045E;
    private const ushort Xbox360ProductId = 0x028E;
    private const ushort Xbox360Version = 0x0114;
    private const string Xbox360Name = "Microsoft X-Box 360 pad";

    // BTN_A, BTN_B, BTN_X, BTN_Y, BTN_TL, BTN_TR, BTN_SELECT, BTN_START, BTN_MODE, BTN_THUMBL, BTN_THUMBR
    private static readonly (StreamPadButtons Button, ushort Key)[] Keys =
    {
        (StreamPadButtons.A, 0x130),
        (StreamPadButtons.B, 0x131),
        (StreamPadButtons.X, 0x133),
        (StreamPadButtons.Y, 0x134),
        (StreamPadButtons.LeftShoulder, 0x136),
        (StreamPadButtons.RightShoulder, 0x137),
        (StreamPadButtons.Back, 0x13A),
        (StreamPadButtons.Start, 0x13B),
        (StreamPadButtons.Guide, 0x13C),
        (StreamPadButtons.LeftStick, 0x13D),
        (StreamPadButtons.RightStick, 0x13E)
    };

    private static readonly (ushort Axis, int Minimum, int Maximum, int Fuzz, int Flat)[] Axes =
    {
        (AxisLeftX, -32768, 32767, 16, 128),
        (AxisLeftY, -32768, 32767, 16, 128),
        (AxisRightX, -32768, 32767, 16, 128),
        (AxisRightY, -32768, 32767, 16, 128),
        (AxisLeftTrigger, 0, 255, 0, 0),
        (AxisRightTrigger, 0, 255, 0, 0),
        (AxisDpadX, -1, 1, 0, 0),
        (AxisDpadY, -1, 1, 0, 0)
    };

    private readonly Action<string> log;

    public string UnavailableReason { get; }

    public UinputPadBackend(Action<string> log)
    {
        this.log = log;
        int probe = open(DevicePath, OpenWriteOnly | OpenNonBlocking);

        if (probe < 0)
        {
            int error = Marshal.GetLastPInvokeError();
            UnavailableReason = error == 13
                ? "this user is not allowed to create controllers (/dev/uinput is not writable). Installing Steam, or its steam-devices package, grants that."
                : $"/dev/uinput could not be opened (error {error}).";
            return;
        }

        close(probe);
    }

    public IStreamVirtualPad CreatePad()
    {
        int device = open(DevicePath, OpenWriteOnly | OpenNonBlocking);

        if (device < 0)
        {
            log?.Invoke($"[Stream] could not open {DevicePath} (error {Marshal.GetLastPInvokeError()}).");
            return null;
        }

        bool ready = ioctl(device, SetEventBit, EventKey) >= 0 && ioctl(device, SetEventBit, EventAbsolute) >= 0;

        foreach ((_, ushort key) in Keys)
        {
            ready = ready && ioctl(device, SetKeyBit, key) >= 0;
        }

        foreach ((ushort axis, int minimum, int maximum, int fuzz, int flat) in Axes)
        {
            // uinput_abs_setup: code (2), padding (2), then value, minimum, maximum, fuzz, flat, resolution.
            byte[] setup = new byte[28];
            BinaryPrimitives.WriteUInt16LittleEndian(setup, axis);
            BinaryPrimitives.WriteInt32LittleEndian(setup.AsSpan(8), minimum);
            BinaryPrimitives.WriteInt32LittleEndian(setup.AsSpan(12), maximum);
            BinaryPrimitives.WriteInt32LittleEndian(setup.AsSpan(16), fuzz);
            BinaryPrimitives.WriteInt32LittleEndian(setup.AsSpan(20), flat);
            ready = ready && ioctl(device, SetAbsoluteBit, axis) >= 0 && ioctl(device, AbsoluteSetup, setup) >= 0;
        }

        // uinput_setup: bus, vendor, product, version (2 each), name (80), force-feedback effects (4).
        byte[] identity = new byte[92];
        BinaryPrimitives.WriteUInt16LittleEndian(identity, BusUsb);
        BinaryPrimitives.WriteUInt16LittleEndian(identity.AsSpan(2), MicrosoftVendorId);
        BinaryPrimitives.WriteUInt16LittleEndian(identity.AsSpan(4), Xbox360ProductId);
        BinaryPrimitives.WriteUInt16LittleEndian(identity.AsSpan(6), Xbox360Version);
        Encoding.ASCII.GetBytes(Xbox360Name).CopyTo(identity, 8);
        ready = ready && ioctl(device, DeviceSetup, identity) >= 0 && ioctl(device, DeviceCreate, 0) >= 0;

        if (!ready)
        {
            log?.Invoke($"[Stream] could not create a virtual controller (error {Marshal.GetLastPInvokeError()}).");
            close(device);
            return null;
        }

        return new Pad(device);
    }

    public void Dispose()
    {
    }

    private sealed class Pad : IStreamVirtualPad
    {
        // input_event on 64-bit Linux: seconds (8), microseconds (8), type (2), code (2), value (4).
        private const int EventSize = 24;

        private readonly byte[] report = new byte[EventSize * (Keys.Length + Axes.Length + 1)];
        private int device;

        public Pad(int device)
        {
            this.device = device;
        }

        public void Submit(in StreamPadState state)
        {
            if (device < 0)
            {
                return;
            }

            int offset = 0;

            foreach ((StreamPadButtons button, ushort key) in Keys)
            {
                offset = WriteEvent(offset, EventKey, key, (state.Buttons & button) != 0 ? 1 : 0);
            }

            // The kernel's vertical axes grow downward.
            offset = WriteEvent(offset, EventAbsolute, AxisLeftX, state.LeftX);
            offset = WriteEvent(offset, EventAbsolute, AxisLeftY, InvertAxis(state.LeftY));
            offset = WriteEvent(offset, EventAbsolute, AxisRightX, state.RightX);
            offset = WriteEvent(offset, EventAbsolute, AxisRightY, InvertAxis(state.RightY));
            offset = WriteEvent(offset, EventAbsolute, AxisLeftTrigger, state.LeftTrigger);
            offset = WriteEvent(offset, EventAbsolute, AxisRightTrigger, state.RightTrigger);
            offset = WriteEvent(offset, EventAbsolute, AxisDpadX, Direction(state.Buttons, StreamPadButtons.DpadLeft, StreamPadButtons.DpadRight));
            offset = WriteEvent(offset, EventAbsolute, AxisDpadY, Direction(state.Buttons, StreamPadButtons.DpadUp, StreamPadButtons.DpadDown));
            offset = WriteEvent(offset, EventSync, 0, 0);
            write(device, report, offset);
        }

        private static int InvertAxis(short value)
        {
            return value == short.MinValue ? short.MaxValue : -value;
        }

        private static int Direction(StreamPadButtons buttons, StreamPadButtons negative, StreamPadButtons positive)
        {
            return ((buttons & positive) != 0 ? 1 : 0) - ((buttons & negative) != 0 ? 1 : 0);
        }

        // The timestamp is left at zero; the kernel stamps events itself.
        private int WriteEvent(int offset, ushort type, ushort code, int value)
        {
            Span<byte> slot = report.AsSpan(offset, EventSize);
            slot.Slice(0, 16).Clear();
            BinaryPrimitives.WriteUInt16LittleEndian(slot.Slice(16), type);
            BinaryPrimitives.WriteUInt16LittleEndian(slot.Slice(18), code);
            BinaryPrimitives.WriteInt32LittleEndian(slot.Slice(20), value);
            return offset + EventSize;
        }

        public void Dispose()
        {
            if (device < 0)
            {
                return;
            }

            ioctl(device, DeviceDestroy, 0);
            close(device);
            device = -1;
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int descriptor);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int descriptor, nuint request, nint value);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int descriptor, nuint request, byte[] data);

    [DllImport("libc", SetLastError = true)]
    private static extern nint write(int descriptor, byte[] data, nint count);
}
