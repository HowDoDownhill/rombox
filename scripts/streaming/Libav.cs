using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

// The few FFmpeg library functions the guest's decoder calls, loaded at run time from
// avcodec/avutil 60 to 63 (FFmpeg 6 to 9). The AVPacket and AVFrame fields read here have kept
// their positions across those versions.
public static class Libav
{
    public const string LibraryFolderVariable = "ROMBOX_FFMPEG_LIBS";
    public const int CodecIdH264 = 27;
    public const int ErrorAgain = -11;
    public const int PixelFormatYuv420p = 0;
    public const int PixelFormatYuvj420p = 12;

    // AVPacket: buf, pts, dts, then data and size.
    public const int PacketDataOffset = 24;
    public const int PacketSizeOffset = 32;

    // AVFrame: data[8], linesize[8], extended_data, then width, height, nb_samples, format.
    public const int FrameLinesizeOffset = 64;
    public const int FrameWidthOffset = 104;
    public const int FrameHeightOffset = 108;
    public const int FrameFormatOffset = 116;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint VersionFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr FindDecoderFn(int codecId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr AllocContextFn(IntPtr codec);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int OpenFn(IntPtr context, IntPtr codec, IntPtr options);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void FreeContextFn(ref IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr AllocFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void FreeFn(ref IntPtr item);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int NewPacketFn(IntPtr packet, int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void UnrefFn(IntPtr item);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int SendPacketFn(IntPtr context, IntPtr packet);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int ReceiveFrameFn(IntPtr context, IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int OptSetFn(IntPtr target, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int searchFlags);

    public static FindDecoderFn FindDecoder;
    public static AllocContextFn AllocContext;
    public static OpenFn Open;
    public static FreeContextFn FreeContext;
    public static AllocFn AllocPacket;
    public static FreeFn FreePacket;
    public static NewPacketFn NewPacket;
    public static UnrefFn UnrefPacket;
    public static AllocFn AllocFrame;
    public static FreeFn FreeFrame;
    public static UnrefFn UnrefFrame;
    public static SendPacketFn SendPacket;
    public static ReceiveFrameFn ReceiveFrame;
    public static OptSetFn OptSet;

    private static bool loaded;
    private static string loadError;

    public static string LoadError => loadError;

    public static bool TryLoad(string appDirectory, Action<string> log)
    {
        if (loaded)
        {
            return true;
        }

        if (loadError != null)
        {
            return false;
        }

        try
        {
            IntPtr avutil = LoadLibrary("avutil", appDirectory);
            IntPtr avcodec = LoadLibrary("avcodec", appDirectory);

            T Get<T>(IntPtr library, string name) => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

            uint version = Get<VersionFn>(avcodec, "avcodec_version")();
            uint major = version >> 16;

            if (major < 60 || major > 63)
            {
                loadError = $"avcodec {major} is not a version RomBox knows (60 to 63).";
                log?.Invoke($"[Stream] {loadError}");
                return false;
            }

            FindDecoder = Get<FindDecoderFn>(avcodec, "avcodec_find_decoder");
            AllocContext = Get<AllocContextFn>(avcodec, "avcodec_alloc_context3");
            Open = Get<OpenFn>(avcodec, "avcodec_open2");
            FreeContext = Get<FreeContextFn>(avcodec, "avcodec_free_context");
            AllocPacket = Get<AllocFn>(avcodec, "av_packet_alloc");
            FreePacket = Get<FreeFn>(avcodec, "av_packet_free");
            NewPacket = Get<NewPacketFn>(avcodec, "av_new_packet");
            UnrefPacket = Get<UnrefFn>(avcodec, "av_packet_unref");
            SendPacket = Get<SendPacketFn>(avcodec, "avcodec_send_packet");
            ReceiveFrame = Get<ReceiveFrameFn>(avcodec, "avcodec_receive_frame");
            AllocFrame = Get<AllocFn>(avutil, "av_frame_alloc");
            FreeFrame = Get<FreeFn>(avutil, "av_frame_free");
            UnrefFrame = Get<UnrefFn>(avutil, "av_frame_unref");
            OptSet = Get<OptSetFn>(avutil, "av_opt_set");

            log?.Invoke($"[Stream] loaded avcodec {major}.{(version >> 8) & 0xFF}.");
            return loaded = true;
        }
        catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException || exception is BadImageFormatException || exception is FileNotFoundException)
        {
            loadError = exception.Message;
            log?.Invoke($"[Stream] the FFmpeg libraries could not be loaded: {exception.Message}");
            return false;
        }
    }

    // ROMBOX_FFMPEG_LIBS, then tools/ffmpeg/<os>/ beside the app, then the system's libraries.
    private static IntPtr LoadLibrary(string name, string appDirectory)
    {
        string platformFolder = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        string baseDirectory = appDirectory ?? Path.GetDirectoryName(Environment.ProcessPath) ?? "";
        string[] folders =
        {
            Environment.GetEnvironmentVariable(LibraryFolderVariable),
            Path.Combine(baseDirectory, "tools", "ffmpeg", platformFolder)
        };

        foreach (string folder in folders.Where(folder => !string.IsNullOrEmpty(folder) && Directory.Exists(folder)))
        {
            string pattern = OperatingSystem.IsWindows() ? $"{name}-*.dll" : OperatingSystem.IsMacOS() ? $"lib{name}.*.dylib" : $"lib{name}.so.*";
            string file = Directory.GetFiles(folder, pattern).OrderByDescending(path => path, StringComparer.Ordinal).FirstOrDefault();

            if (file != null)
            {
                return NativeLibrary.Load(Path.GetFullPath(file), typeof(Libav).Assembly, DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
            }
        }

        int[] majors = name == "avutil" ? new[] { 61, 60, 59, 58 } : new[] { 63, 62, 61, 60 };

        foreach (int major in majors)
        {
            string fileName = OperatingSystem.IsWindows() ? $"{name}-{major}.dll" : OperatingSystem.IsMacOS() ? $"lib{name}.{major}.dylib" : $"lib{name}.so.{major}";

            if (NativeLibrary.TryLoad(fileName, out IntPtr handle))
            {
                return handle;
            }
        }

        throw new DllNotFoundException($"No {name} library was found. Set {LibraryFolderVariable} to a folder holding FFmpeg's shared libraries.");
    }
}
