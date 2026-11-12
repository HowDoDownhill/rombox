using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

// Windows: records only what one program and the programs it started are playing, through
// WASAPI's process loopback (Windows 10 version 2004 and later). The host's voice chat, music and
// notifications stay out of the stream. This file must only be reached on Windows; see
// StreamAudioCaptures.CreateWindows.
public sealed class WindowsProcessAudioCapture : IStreamAudioCapture
{
    private const string ProcessLoopbackDevice = @"VAD\Process_Loopback";
    private const int ActivationTypeProcessLoopback = 1;
    private const int LoopbackModeIncludeProcessTree = 0;
    private const ushort VariantTypeBlob = 65;

    private const int ShareModeShared = 0;
    private const uint StreamFlagLoopback = 0x00020000;
    private const uint StreamFlagEventCallback = 0x00040000;
    private const uint StreamFlagAutoConvertPcm = 0x80000000;
    private const uint StreamFlagDefaultResamplerQuality = 0x08000000;
    private const uint BufferFlagSilent = 0x2;
    private const long BufferDurationHundredNanoseconds = 200000;

    private static readonly Guid AudioClientId = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid AudioCaptureClientId = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

    private readonly StreamCaptureTarget target;
    private readonly Action<string> log;
    private Thread captureThread;
    private volatile bool stopping;

    public event Action<short[], int> SamplesCaptured;

    public WindowsProcessAudioCapture(StreamCaptureTarget target, Action<string> log)
    {
        this.target = target;
        this.log = log;
    }

    public bool Start()
    {
        int processId = FindProcessId(target);

        if (processId == 0)
        {
            log?.Invoke($"[Stream] no running program matches {target}; the stream has no sound.");
            return false;
        }

        captureThread = new Thread(() => Capture(processId)) { IsBackground = true, Name = "Stream audio capture" };
        captureThread.Start();
        return true;
    }

    private void Capture(int processId)
    {
        IAudioClient client = null;
        IAudioCaptureClient captureClient = null;

        try
        {
            client = Activate(processId);

            if (client == null)
            {
                return;
            }

            var format = new WaveFormat
            {
                FormatTag = 1,
                Channels = StreamAudioFormat.Channels,
                SamplesPerSecond = StreamAudioFormat.SampleRate,
                AverageBytesPerSecond = StreamAudioFormat.SampleRate * StreamAudioFormat.Channels * 2,
                BlockAlign = StreamAudioFormat.Channels * 2,
                BitsPerSample = 16
            };

            using var dataReady = new AutoResetEvent(false);
            client.Initialize(ShareModeShared, StreamFlagLoopback | StreamFlagEventCallback | StreamFlagAutoConvertPcm | StreamFlagDefaultResamplerQuality,
                BufferDurationHundredNanoseconds, 0, ref format, IntPtr.Zero);
            client.SetEventHandle(dataReady.SafeWaitHandle.DangerousGetHandle());
            Guid captureId = AudioCaptureClientId;
            client.GetService(ref captureId, out object service);
            captureClient = (IAudioCaptureClient)service;
            client.Start();
            log?.Invoke($"[Stream] capturing sound from process {processId} and the programs it starts.");

            short[] samples = new short[StreamAudioFormat.SampleRate * StreamAudioFormat.Channels / 10];

            while (!stopping)
            {
                dataReady.WaitOne(200);

                while (!stopping)
                {
                    captureClient.GetNextPacketSize(out uint packetFrames);

                    if (packetFrames == 0)
                    {
                        break;
                    }

                    captureClient.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _);
                    int count = (int)frames * StreamAudioFormat.Channels;

                    if (count > samples.Length)
                    {
                        samples = new short[count];
                    }

                    if ((flags & BufferFlagSilent) != 0)
                    {
                        Array.Clear(samples, 0, count);
                    }
                    else
                    {
                        Marshal.Copy(data, samples, 0, count);
                    }

                    captureClient.ReleaseBuffer(frames);
                    SamplesCaptured?.Invoke(samples, count);
                }
            }

            client.Stop();
        }
        catch (Exception failure)
        {
            log?.Invoke($"[Stream] sound capture stopped: {failure.GetType().Name}: {failure.Message}");
        }
        finally
        {
            if (captureClient != null) Marshal.ReleaseComObject(captureClient);
            if (client != null) Marshal.ReleaseComObject(client);
        }
    }

    private IAudioClient Activate(int processId)
    {
        // A PROPVARIANT holding a blob: type (2), padding (6), blob size (4), padding (4), blob pointer (8).
        IntPtr activation = Marshal.AllocHGlobal(12);
        IntPtr variant = Marshal.AllocHGlobal(24);

        try
        {
            Marshal.WriteInt32(activation, 0, ActivationTypeProcessLoopback);
            Marshal.WriteInt32(activation, 4, processId);
            Marshal.WriteInt32(activation, 8, LoopbackModeIncludeProcessTree);

            for (int offset = 0; offset < 24; offset += 4)
            {
                Marshal.WriteInt32(variant, offset, 0);
            }

            Marshal.WriteInt16(variant, 0, (short)VariantTypeBlob);
            Marshal.WriteInt32(variant, 8, 12);
            Marshal.WriteIntPtr(variant, 16, activation);

            var handler = new ActivationHandler();
            ActivateAudioInterfaceAsync(ProcessLoopbackDevice, AudioClientId, variant, handler, out IActivateAudioInterfaceAsyncOperation operation);

            if (!handler.Completed.WaitOne(5000))
            {
                log?.Invoke("[Stream] Windows did not answer the request to capture the game's sound.");
                return null;
            }

            operation.GetActivateResult(out int result, out object activated);

            if (result < 0 || activated == null)
            {
                log?.Invoke($"[Stream] Windows refused to capture the game's sound (error 0x{result:X8}). Capturing one program's sound needs Windows 10 version 2004 or later.");
                return null;
            }

            return (IAudioClient)activated;
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
            Marshal.FreeHGlobal(activation);
        }
    }

    // The size of the target's window contents in pixels, or (0, 0) when it cannot be found.
    public static (int Width, int Height) FindWindowSize(StreamCaptureTarget target)
    {
        IntPtr window = new IntPtr(target.WindowHandle);

        if (window == IntPtr.Zero)
        {
            try
            {
                using Process process = Process.GetProcessById(FindProcessId(target));
                window = process.MainWindowHandle;
            }
            catch (Exception)
            {
                return (0, 0);
            }
        }

        return window != IntPtr.Zero && GetClientRect(window, out WindowRectangle rectangle)
            ? (rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top)
            : (0, 0);
    }

    private static int FindProcessId(StreamCaptureTarget target)
    {
        if (target.WindowHandle != 0)
        {
            GetWindowThreadProcessId(new IntPtr(target.WindowHandle), out uint windowProcessId);
            return (int)windowProcessId;
        }

        Process[] processes = Process.GetProcesses();

        try
        {
            if (!string.IsNullOrEmpty(target.WindowExe))
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(target.WindowExe);
                return processes.FirstOrDefault(process => string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase))?.Id ?? 0;
            }

            if (!string.IsNullOrEmpty(target.WindowTitle))
            {
                var title = new Regex(target.WindowTitle);
                return processes.FirstOrDefault(process => HasTitle(process, title))?.Id ?? 0;
            }

            return 0;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static bool HasTitle(Process process, Regex title)
    {
        try
        {
            return !string.IsNullOrEmpty(process.MainWindowTitle) && title.IsMatch(process.MainWindowTitle);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        stopping = true;
        captureThread?.Join(1000);
    }

    private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly ManualResetEvent Completed = new ManualResetEvent(false);

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            Completed.Set();
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
        IntPtr activationParameters,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation operation);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out WindowRectangle rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        void GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    // Marks the handler as safe to call from any thread, which Windows requires here.
    [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject
    {
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        void Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, ref WaveFormat format, IntPtr audioSessionId);
        void GetBufferSize(out uint bufferFrames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out uint paddingFrames);
        [PreserveSig] int IsFormatSupported(int shareMode, ref WaveFormat format, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr eventHandle);
        void GetService(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        void GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong performanceCounterPosition);
        void ReleaseBuffer(uint frames);
        void GetNextPacketSize(out uint frames);
    }
}
