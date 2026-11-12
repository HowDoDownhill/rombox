using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

// Linux: records everything the default output device plays, through ffmpeg's PulseAudio input
// (PipeWire answers it too). The whole desktop's sound is captured, the host's other programs
// included.
public sealed class PulseAudioCapture : IStreamAudioCapture
{
    // PulseAudio's name for "the monitor of whichever output is the default".
    private const string DefaultMonitor = "@DEFAULT_MONITOR@";

    private readonly string ffmpegPath;
    private readonly Action<string> log;
    private Process process;
    private Thread readerThread;
    private volatile bool stopping;

    public event Action<short[], int> SamplesCaptured;

    public PulseAudioCapture(string ffmpegPath, Action<string> log)
    {
        this.ffmpegPath = ffmpegPath;
        this.log = log;
    }

    public bool Start()
    {
        // 10 ms fragments; without the setting PulseAudio hands audio over in much larger pieces.
        int fragmentBytes = StreamAudioFormat.FrameSamples * StreamAudioFormat.Channels * 2;
        string arguments = $"-hide_banner -loglevel warning -nostats -f pulse -fragment_size {fragmentBytes} -sample_rate {StreamAudioFormat.SampleRate} -channels {StreamAudioFormat.Channels} "
            + $"-i {DefaultMonitor} -ac {StreamAudioFormat.Channels} -ar {StreamAudioFormat.SampleRate} -f s16le -flush_packets 1 pipe:1";

        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            });
        }
        catch (Exception failure)
        {
            log?.Invoke($"[Stream] sound capture did not start: {failure.Message}");
            return false;
        }

        process.ErrorDataReceived += (_, line) =>
        {
            if (!string.IsNullOrWhiteSpace(line.Data))
            {
                log?.Invoke($"[Stream] ffmpeg (sound): {line.Data}");
            }
        };
        process.BeginErrorReadLine();

        readerThread = new Thread(Read) { IsBackground = true, Name = "Stream audio capture" };
        readerThread.Start();
        log?.Invoke("[Stream] capturing the desktop's sound.");
        return true;
    }

    private void Read()
    {
        Stream output = process.StandardOutput.BaseStream;
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

        if (!stopping)
        {
            log?.Invoke("[Stream] sound capture ended.");
        }
    }

    public void Dispose()
    {
        stopping = true;

        try
        {
            if (process != null && !process.HasExited)
            {
                process.StandardInput.Write('q');
                process.StandardInput.Flush();

                if (!process.WaitForExit(500))
                {
                    process.Kill();
                }
            }
        }
        catch (Exception)
        {
        }

        process?.Dispose();
        process = null;
    }
}
