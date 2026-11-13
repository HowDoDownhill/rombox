using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

// Linux sound capture through PulseAudio, which PipeWire answers too.
//
// Given the game's process, only the game is recorded: `pactl` lists what each program is
// playing, and `parec --monitor-stream` records one of those. The host's other programs stay out
// of the stream, and the host still hears the game as usual. The game opens its sound a moment
// after it starts and may reopen it later, so the capture looks for it again whenever it is gone.
//
// Without a process, or without those two tools, everything the default output plays is recorded
// through ffmpeg.
public sealed class PulseAudioCapture : IStreamAudioCapture
{
    // PulseAudio's name for "the monitor of whichever output is the default".
    private const string DefaultMonitor = "@DEFAULT_MONITOR@";
    private const int LookAgainMs = 1000;
    // PipeWire runs every program's sound at the pace of the most demanding one. Asking for
    // 10 ms pieces made the whole system work in pieces that small, and the game's own sound
    // stuttered on the host. 22 ms is just above PipeWire's usual pace, so nothing changes for
    // the game.
    private const int GameRecordingLatencyMs = 22;

    private readonly string ffmpegPath;
    private readonly int processId;
    private readonly Action<string> log;
    private readonly object processLock = new object();
    private Process process;
    private Thread readerThread;
    private volatile bool stopping;

    public event Action<short[], int> SamplesCaptured;

    public PulseAudioCapture(string ffmpegPath, int processId, Action<string> log)
    {
        this.ffmpegPath = ffmpegPath;
        this.processId = processId;
        this.log = log;
    }

    public bool Start()
    {
        bool recordsGameOnly = processId > 0 && HasTool("pactl") && HasTool("parec");

        if (processId > 0 && !recordsGameOnly)
        {
            log?.Invoke("[Stream] pactl and parec were not found, so the whole desktop's sound is sent. They come with PulseAudio's or PipeWire's tools.");
        }

        readerThread = new Thread(recordsGameOnly ? RecordGame : RecordDesktop) { IsBackground = true, Name = "Stream audio capture" };
        readerThread.Start();
        return true;
    }

    private void RecordDesktop()
    {
        // 10 ms fragments; without the setting PulseAudio hands audio over in much larger pieces.
        int fragmentBytes = StreamAudioFormat.FrameSamples * StreamAudioFormat.Channels * 2;
        string arguments = $"-hide_banner -loglevel warning -nostats -f pulse -fragment_size {fragmentBytes} -sample_rate {StreamAudioFormat.SampleRate} -channels {StreamAudioFormat.Channels} "
            + $"-i {DefaultMonitor} -ac {StreamAudioFormat.Channels} -ar {StreamAudioFormat.SampleRate} -f s16le -flush_packets 1 pipe:1";

        if (Run(ffmpegPath, arguments))
        {
            log?.Invoke("[Stream] capturing the desktop's sound.");
            Read();
        }

        if (!stopping)
        {
            log?.Invoke("[Stream] sound capture ended.");
        }
    }

    private void RecordGame()
    {
        bool reportedWaiting = false;

        while (!stopping)
        {
            int stream = FindStream();

            if (stream < 0)
            {
                if (!reportedWaiting)
                {
                    reportedWaiting = true;
                    log?.Invoke("[Stream] waiting for the game to start playing sound.");
                }

                Thread.Sleep(LookAgainMs);
                continue;
            }

            string arguments = $"--monitor-stream={stream} --raw --format=s16le --rate={StreamAudioFormat.SampleRate} --channels={StreamAudioFormat.Channels} --latency-msec={GameRecordingLatencyMs}";

            if (!Run("parec", arguments))
            {
                return;
            }

            log?.Invoke($"[Stream] capturing the game's sound (stream {stream}).");
            reportedWaiting = false;
            Read();
        }
    }

    // The number PulseAudio gave the sound the game is playing, or -1. The game may run as a
    // child of the program RomBox started.
    //
    // A sound's entry names its owner in one of two ways. PulseAudio programs carry
    // application.process.id on the sound itself. Programs that speak to PipeWire directly carry
    // only "Client: 134", and the process id is on that client's own entry.
    private int FindStream()
    {
        HashSet<int> family;
        string clients;
        string sounds;

        try
        {
            family = X11Windows.ProcessFamily(processId);
            clients = List("clients");
            sounds = List("sink-inputs");
        }
        catch (Exception)
        {
            return -1;
        }

        var familyClients = new HashSet<int>();
        int current = -1;

        foreach (string rawLine in clients.Split('\n'))
        {
            string line = rawLine.Trim();

            if (line.StartsWith("Client #"))
            {
                current = int.TryParse(line.Substring("Client #".Length), out int index) ? index : -1;
            }
            else if (current >= 0 && IsOwnedByFamily(line, family))
            {
                familyClients.Add(current);
            }
        }

        current = -1;

        foreach (string rawLine in sounds.Split('\n'))
        {
            string line = rawLine.Trim();

            if (line.StartsWith("Sink Input #"))
            {
                current = int.TryParse(line.Substring("Sink Input #".Length), out int index) ? index : -1;
            }
            else if (current >= 0 && IsOwnedByFamily(line, family))
            {
                return current;
            }
            else if (current >= 0 && line.StartsWith("Client: ") && int.TryParse(line.Substring("Client: ".Length), out int client) && familyClients.Contains(client))
            {
                return current;
            }
        }

        return -1;
    }

    // True for a line such as: application.process.id = "3456"
    private static bool IsOwnedByFamily(string line, HashSet<int> family)
    {
        foreach (string property in new[] { "application.process.id = ", "pipewire.sec.pid = " })
        {
            if (line.StartsWith(property) && int.TryParse(line.Substring(property.Length).Trim('"'), out int owner) && family.Contains(owner))
            {
                return true;
            }
        }

        return false;
    }

    private static string List(string what)
    {
        using Process pactl = Process.Start(new ProcessStartInfo
        {
            FileName = "pactl",
            Arguments = "list " + what,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Environment = { ["LC_ALL"] = "C" }
        });

        string listing = pactl.StandardOutput.ReadToEnd();
        pactl.WaitForExit(2000);
        return listing;
    }

    private static bool HasTool(string name)
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
        {
            if (folder.Length > 0 && File.Exists(Path.Combine(folder, name)))
            {
                return true;
            }
        }

        return false;
    }

    private bool Run(string fileName, string arguments)
    {
        try
        {
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            started.ErrorDataReceived += (_, line) =>
            {
                if (!string.IsNullOrWhiteSpace(line.Data))
                {
                    log?.Invoke($"[Stream] {fileName} (sound): {line.Data}");
                }
            };
            started.BeginErrorReadLine();

            lock (processLock)
            {
                process = started;
            }

            return true;
        }
        catch (Exception failure)
        {
            log?.Invoke($"[Stream] sound capture did not start: {failure.Message}");
            return false;
        }
    }

    // Reads until the recording program ends.
    private void Read()
    {
        Process current = process;
        Stream output = current.StandardOutput.BaseStream;
        byte[] bytes = new byte[StreamAudioFormat.FrameSamples * StreamAudioFormat.Channels * 2];
        short[] samples = new short[bytes.Length / 2];
        int filled = 0;

        try
        {
            while (!stopping)
            {
                int read = output.Read(bytes, filled, bytes.Length - filled);

                if (read <= 0)
                {
                    break;
                }

                filled += read;
                // A read can end in the middle of a sample; the odd bytes wait for the next read.
                int whole = filled & ~3;

                if (whole == 0)
                {
                    continue;
                }

                MemoryMarshal.Cast<byte, short>(bytes.AsSpan(0, whole)).CopyTo(samples);
                Buffer.BlockCopy(bytes, whole, bytes, 0, filled - whole);
                filled -= whole;
                SamplesCaptured?.Invoke(samples, whole / 2);
            }
        }
        catch (Exception)
        {
        }

        Stop(current);
    }

    private void Stop(Process target)
    {
        lock (processLock)
        {
            if (process == target)
            {
                process = null;
            }
        }

        try
        {
            if (!target.HasExited)
            {
                target.Kill();
            }
        }
        catch (Exception)
        {
        }

        target.Dispose();
    }

    public void Dispose()
    {
        stopping = true;
        Process current;

        lock (processLock)
        {
            current = process;
        }

        if (current != null)
        {
            Stop(current);
        }
    }
}
