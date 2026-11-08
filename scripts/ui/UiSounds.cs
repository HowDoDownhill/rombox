using Godot;
using System.Collections.Generic;

// Plays the interface sounds: a tick when the selection moves, rising and falling notes for
// Select and Back, and a sweep when the system changes. A move only sounds shortly after the
// player pressed something, so focus changes made by code at startup or on a reload stay quiet.
public partial class UiSounds : Node
{
    public enum Sound { Move, Select, Back, Switch }

    private const float VolumeDb = -8.0f;
    private const ulong UserInputWindowMilliseconds = 400;
    private const ulong MinimumMoveGapMilliseconds = 35;
    // After Select, Back or Switch, the focus change that follows would tick over the first sound.
    private const ulong QuietAfterActionMilliseconds = 200;

    private static readonly Dictionary<Sound, string> Paths = new Dictionary<Sound, string>
    {
        { Sound.Move, "res://assets/sounds/move.wav" },
        { Sound.Select, "res://assets/sounds/select.wav" },
        { Sound.Back, "res://assets/sounds/back.wav" },
        { Sound.Switch, "res://assets/sounds/switch.wav" }
    };

    private static UiSounds instance;

    private readonly Dictionary<Sound, AudioStreamPlayer> players = new Dictionary<Sound, AudioStreamPlayer>();
    private ConfigManager configManager;
    private ulong lastInputAt;
    private ulong lastMoveAt;
    private ulong lastActionAt;

    public UiSounds(ConfigManager configManager)
    {
        this.configManager = configManager;
        Name = "UiSounds";
        ProcessMode = ProcessModeEnum.Always;
    }

    public UiSounds()
    {
    }

    public override void _Ready()
    {
        instance = this;

        foreach (var entry in Paths)
        {
            var player = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(entry.Value), VolumeDb = VolumeDb };
            AddChild(player);
            players[entry.Key] = player;
        }

        // The window sees every event before any node can mark it handled.
        GetTree().Root.WindowInput += OnWindowInput;
        GetViewport().GuiFocusChanged += _ => Play(Sound.Move);
    }

    public override void _ExitTree()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public static void Play(Sound sound)
    {
        instance?.PlaySound(sound);
    }

    private void OnWindowInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseMotion || !inputEvent.IsPressed())
        {
            return;
        }

        lastInputAt = Time.GetTicksMsec();

        if (inputEvent.IsEcho())
        {
            return;
        }

        if (inputEvent.IsActionPressed("ui_accept") || inputEvent.IsActionPressed("Select"))
        {
            PlaySound(Sound.Select);
        }
        else if (inputEvent.IsActionPressed("ui_cancel") || inputEvent.IsActionPressed("Back"))
        {
            PlaySound(Sound.Back);
        }
    }

    private void PlaySound(Sound sound)
    {
        if (configManager?.UiSoundsEnabled != true || !players.TryGetValue(sound, out AudioStreamPlayer player))
        {
            return;
        }

        ulong now = Time.GetTicksMsec();

        if (sound == Sound.Move)
        {
            if (now - lastInputAt > UserInputWindowMilliseconds || now - lastActionAt < QuietAfterActionMilliseconds || now - lastMoveAt < MinimumMoveGapMilliseconds)
            {
                return;
            }

            lastMoveAt = now;
        }
        else
        {
            if (now - lastInputAt > UserInputWindowMilliseconds)
            {
                return;
            }

            lastActionAt = now;
        }

        player.Play();
    }
}
