using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum InputLayerConsent
{
    Unasked,
    SteamInput,
    Declined
}

public class SessionPlayer
{
    public int PhysicalDeviceId;
    public ushort Vendor;
    public ushort Product;
}

public partial class InputLayer : Node
{
    public const int MaximumPlayers = 4;

    private const ushort SteamVirtualPadVendorId = 0x28DE;
    private const ushort SteamVirtualPadProductId = 0x11FF;

    private AppInstance appInstance;
    private ControllerManager controllerManager;

    private readonly List<SessionPlayer> sessionPlayers = new List<SessionPlayer>();

    public bool IsSessionActive { get; private set; }
    public bool IsSteamInputSession { get; private set; }
    public string LastSessionFailureReason { get; private set; } = "";

    public override void _Ready()
    {
        appInstance = GetNode<AppInstance>("/root/AppInstance");
        appInstance.inputLayer = this;
        controllerManager = GetNode<ControllerManager>("/root/ControllerManager");

        Input.JoyConnectionChanged += OnJoyConnectionChanged;
    }

    public int ActivePlayerCount => sessionPlayers.Count;
    public IReadOnlyList<SessionPlayer> SessionPlayers => sessionPlayers;

    public int ResolvePlayerOneDeviceId()
    {
        if (sessionPlayers.Count > 0)
        {
            return sessionPlayers[0].PhysicalDeviceId;
        }

        List<ConnectedController> availablePads = GetAssignablePhysicalPads();
        return availablePads.Count > 0 ? availablePads[0].GodotDeviceId : -1;
    }

    private readonly List<int> playerAssignmentDeviceIds = new List<int>();

    public IReadOnlyList<int> PlayerAssignmentDeviceIds => playerAssignmentDeviceIds;

    public void SetPlayerAssignment(IEnumerable<int> orderedDeviceIds)
    {
        playerAssignmentDeviceIds.Clear();
        playerAssignmentDeviceIds.AddRange(orderedDeviceIds);
        GD.Print($"[InputLayer] player order set to devices [{string.Join(", ", playerAssignmentDeviceIds)}].");
    }

    public void ClearPlayerAssignment()
    {
        if (playerAssignmentDeviceIds.Count == 0)
        {
            return;
        }

        playerAssignmentDeviceIds.Clear();
        GD.Print("[InputLayer] player order cleared; falling back to connection order.");
    }

    private List<ConnectedController> GetAssignablePhysicalPads()
    {
        return controllerManager.GetConnectedControllers().OrderBy(ResolveAssignedPlayerPosition).ToList();
    }

    public bool IsMappingEnabled => OperatingSystem.IsLinux()
        ? ResolveConsent() != InputLayerConsent.Declined
        : ResolveConsent() == InputLayerConsent.SteamInput && ControllerSetupMethod.IsLaunchedThroughSteam;

    public bool UsesSteamInput => OperatingSystem.IsLinux()
        ? IsMappingEnabled && ControllerSetupMethod.IsLaunchedThroughSteam && IsSteamVirtualPadConnected()
        : IsMappingEnabled;

    public bool CanAssignPlayers => IsMappingEnabled && !UsesSteamInput;

    private bool IsSteamVirtualPadConnected()
    {
        return controllerManager.GetConnectedControllers().Any(pad => ReadVendorAndProduct(pad.GodotDeviceId) == (SteamVirtualPadVendorId, SteamVirtualPadProductId));
    }

    private int ResolveAssignedPlayerPosition(ConnectedController pad)
    {
        if (UsesSteamInput)
        {
            return pad.ConnectionOrder;
        }

        int assignedPosition = playerAssignmentDeviceIds.IndexOf(pad.GodotDeviceId);
        return assignedPosition >= 0 ? assignedPosition : playerAssignmentDeviceIds.Count + pad.ConnectionOrder;
    }

    public static int ReadXInputSlot(int godotDeviceId)
    {
        Godot.Collections.Dictionary joypadInfo = Input.GetJoyInfo(godotDeviceId);
        return joypadInfo.TryGetValue("xinput_index", out Variant slot) && int.TryParse(slot.AsString(), out int slotIndex) ? slotIndex : -1;
    }

    public static (ushort Vendor, ushort Product) ReadVendorAndProduct(int godotDeviceId)
    {
        Godot.Collections.Dictionary joypadInfo = Input.GetJoyInfo(godotDeviceId);
        ushort ReadId(string key) => joypadInfo.TryGetValue(key, out Variant value) && ushort.TryParse(value.AsString(), out ushort id) ? id : (ushort)0;
        return (ReadId("vendor_id"), ReadId("product_id"));
    }

    public bool BeginSession(string systemSlug, EmulatorMeta emulatorMetadata)
    {
        EndSession();

        if (!IsMappingEnabled)
        {
            LastSessionFailureReason = ResolveConsent() == InputLayerConsent.SteamInput
                ? "the app was not launched through Steam"
                : "automatic controller mapping is set to Manual";
            GD.Print($"[InputLayer] not starting a session: {LastSessionFailureReason}.");
            return false;
        }

        List<ConnectedController> physicalPads = GetAssignablePhysicalPads();

        if (physicalPads.Count == 0)
        {
            LastSessionFailureReason = "no controller is connected";
            GD.Print($"[InputLayer] not starting a session: {LastSessionFailureReason}.");
            return false;
        }

        sessionPlayers.AddRange(physicalPads.Take(ResolveMaximumPlayers(emulatorMetadata)).Select(DescribePlayer));

        IsSessionActive = true;
        IsSteamInputSession = UsesSteamInput;
        LastSessionFailureReason = "";

        GD.Print($"[InputLayer] session started for '{systemSlug}' with {sessionPlayers.Count} player(s){(IsSteamInputSession ? ", Steam Input" : "")}: {string.Join("; ", sessionPlayers.Select(DescribePlayerForLog))}.");
        return true;
    }

    private static SessionPlayer DescribePlayer(ConnectedController pad)
    {
        (ushort vendor, ushort product) = ReadVendorAndProduct(pad.GodotDeviceId);

        return new SessionPlayer
        {
            PhysicalDeviceId = pad.GodotDeviceId,
            Vendor = vendor,
            Product = product
        };
    }

    private static string DescribePlayerForLog(SessionPlayer player, int playerIndex)
    {
        return $"P{playerIndex + 1} device {player.PhysicalDeviceId} \"{Input.GetJoyName(player.PhysicalDeviceId)}\" {player.Vendor:X4}/{player.Product:X4}";
    }

    public int ResolvePlayerXInputSlot(int playerIndex)
    {
        return playerIndex >= 0 && playerIndex < sessionPlayers.Count ? playerIndex : -1;
    }

    public (ushort Vendor, ushort Product) ResolvePlayerIdentity(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= sessionPlayers.Count)
        {
            return (0, 0);
        }

        return (sessionPlayers[playerIndex].Vendor, sessionPlayers[playerIndex].Product);
    }

    public string ResolvePlayerGuid(int playerIndex)
    {
        return playerIndex >= 0 && playerIndex < sessionPlayers.Count ? Input.GetJoyGuid(sessionPlayers[playerIndex].PhysicalDeviceId) : "";
    }

    private int ResolveMaximumPlayers(EmulatorMeta emulatorMetadata)
    {
        int metadataMaximum = emulatorMetadata?.ControllerConfig?.MaxControllers ?? 0;
        return metadataMaximum > 0 ? Math.Min(metadataMaximum, MaximumPlayers) : MaximumPlayers;
    }

    public void EndSession()
    {
        if (!IsSessionActive && sessionPlayers.Count == 0)
        {
            return;
        }

        IsSessionActive = false;
        IsSteamInputSession = false;
        sessionPlayers.Clear();

        GD.Print("[InputLayer] session ended.");
    }

    private void OnJoyConnectionChanged(long deviceId, bool connected)
    {
        int joypadId = (int)deviceId;

        if (!connected && playerAssignmentDeviceIds.Remove(joypadId))
        {
            GD.Print($"[InputLayer] device {joypadId} left; player order is now [{string.Join(", ", playerAssignmentDeviceIds)}].");
        }
    }

    public InputLayerConsent ResolveConsent()
    {
        string storedConsent = appInstance?.configManager?.ControllerMappingConsent;

        switch (storedConsent)
        {
            case ConfigManager.ControllerMappingConsentSteamInput: return InputLayerConsent.SteamInput;
            case ConfigManager.ControllerMappingConsentDeclined: return InputLayerConsent.Declined;
            default: return InputLayerConsent.Unasked;
        }
    }

    public override void _ExitTree()
    {
        Input.JoyConnectionChanged -= OnJoyConnectionChanged;
        EndSession();
    }
}
