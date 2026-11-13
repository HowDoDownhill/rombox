using Godot;
using System.Linq;
using System.Net;

// Test switches for game streaming, given after "--" on the command line:
//   --stream-host=<target>   capture a window and stream it. Targets: exe:retroarch.exe,
//                            title:<regular expression>, hwnd:<handle>, x11:<window id>
//   --stream-join=<address>  show a host's stream, as host or host:port
//   --stream-port=<port>     the UDP port to host on (default 55450)
//   --stream-bitrate=<kbps>  with --stream-host: picture bitrate in kbit/s (default 10000)
//   --stream-encoder=<name>  with --stream-host: use this ffmpeg encoder, such as h264_vaapi or
//                            libx264, in place of the one RomBox would pick
//   --stream-test-input      with --stream-join: send a made-up controller that presses A twice
//                            a second and swings the left stick
//   --stream-no-input        with --stream-join: send no controllers, for tests beside a real session
//   --stream-window=<x,y,w,h> put the RomBox window there instead of full screen, for tests
public static class StreamCommandLine
{
    private const string HostPrefix = "--stream-host=";
    private const string JoinPrefix = "--stream-join=";
    private const string PortPrefix = "--stream-port=";
    private const string BitratePrefix = "--stream-bitrate=";
    private const string EncoderPrefix = "--stream-encoder=";
    private const string TestInputSwitch = "--stream-test-input";
    private const string NoInputSwitch = "--stream-no-input";
    private const string WindowPrefix = "--stream-window=";

    public static void StartIfRequested(Node parent)
    {
        string[] arguments = OS.GetCmdlineUserArgs();
        string hostTarget = Read(arguments, HostPrefix);
        string joinAddress = Read(arguments, JoinPrefix);
        int port = int.TryParse(Read(arguments, PortPrefix), out int parsedPort) ? parsedPort : StreamProtocol.DefaultPort;
        PlaceWindow(Read(arguments, WindowPrefix));

        if (!string.IsNullOrEmpty(hostTarget))
        {
            parent.GetTree().Root.CallDeferred(Node.MethodName.AddChild, new StreamHostNode(port, StreamCaptureTarget.Parse(hostTarget), new StreamEncoderSettings { BitrateKbps = int.TryParse(Read(arguments, BitratePrefix), out int bitrate) ? bitrate : new StreamEncoderSettings().BitrateKbps, Encoder = Read(arguments, EncoderPrefix) }));
        }

        if (!string.IsNullOrEmpty(joinAddress))
        {
            if (IPEndPoint.TryParse(joinAddress, out IPEndPoint endPoint))
            {
                if (endPoint.Port == 0)
                {
                    endPoint.Port = StreamProtocol.DefaultPort;
                }

                parent.GetTree().Root.CallDeferred(Node.MethodName.AddChild, new StreamGuestView(endPoint) { SendsTestInput = arguments.Contains(TestInputSwitch), SendsNoInput = arguments.Contains(NoInputSwitch) });
            }
            else
            {
                GD.PrintErr($"[Stream] {joinAddress} is not an address; use an IP address such as 192.168.1.20.");
            }
        }
    }

    private static void PlaceWindow(string placement)
    {
        int[] parts = (placement ?? "").Split(',').Select(part => int.TryParse(part, out int value) ? value : -1).ToArray();

        if (parts.Length != 4 || parts[2] <= 0 || parts[3] <= 0)
        {
            return;
        }

        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(new Vector2I(parts[2], parts[3]));
        DisplayServer.WindowSetPosition(new Vector2I(parts[0], parts[1]));
        // Test windows stay in front, so a measurement that reads them off the screen still works
        // when another window is in the way.
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);
    }

    private static string Read(string[] arguments, string prefix)
    {
        return arguments.FirstOrDefault(argument => argument.StartsWith(prefix))?.Substring(prefix.Length);
    }
}
