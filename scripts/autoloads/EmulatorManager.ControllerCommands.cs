using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public class ControllerCommands
{
    [JsonPropertyName("platforms")]
    public List<string> Platforms { get; set; }

    [JsonPropertyName("list_args")]
    public string ListArgs { get; set; }

    [JsonPropertyName("list_pattern")]
    public string ListPattern { get; set; }

    [JsonPropertyName("assign_args")]
    public string AssignArgs { get; set; }

    [JsonPropertyName("first_controller_number")]
    public int FirstControllerNumber { get; set; }

    [JsonPropertyName("port_count")]
    public int PortCount { get; set; }

    [JsonPropertyName("config_file_relative_path")]
    public string ConfigFileRelativePath { get; set; }

    [JsonPropertyName("assignment_json_path")]
    public string AssignmentJsonPath { get; set; }

    [JsonPropertyName("enabled_json_path")]
    public string EnabledJsonPath { get; set; }
}

public partial class EmulatorManager
{
    private const int ControllerCommandTimeoutMilliseconds = 15000;

    private class ListedController
    {
        public int Number;
        public string Name;
    }

    private async Task AssignControllersThroughEmulatorCommands(EmulatorMeta emulatorMetadata, string emulatorInstallDirectory, string executablePath)
    {
        InputLayer inputLayer = appInstance?.inputLayer;
        ControllerCommands commands = emulatorMetadata?.ControllerCommands;

        if (inputLayer == null || !inputLayer.IsSessionActive || commands == null || commands.Platforms == null || !commands.Platforms.Contains(OS.GetName().ToLower()))
        {
            return;
        }

        string listOutput = await RunControllerCommand(emulatorMetadata, emulatorInstallDirectory, executablePath, commands.ListArgs);

        if (listOutput == null)
        {
            return;
        }

        var listPattern = new Regex(commands.ListPattern, RegexOptions.Multiline);
        List<ListedController> listedControllers = listPattern.Matches(listOutput)
            .Select(match => new ListedController { Number = int.Parse(match.Groups[1].Value), Name = match.Groups[2].Value.Trim() })
            .Where(listed => listed.Number >= commands.FirstControllerNumber)
            .ToList();

        GD.Print($"[InputLayer] {emulatorMetadata.Name} lists {listedControllers.Count} controller(s): {string.Join("; ", listedControllers.Select(listed => $"{listed.Number} \"{listed.Name}\""))}");

        int playerCount = Math.Min(inputLayer.ActivePlayerCount, commands.PortCount);
        var claimedNumbers = new HashSet<int>();
        var playerControllers = new ListedController[playerCount];
        List<int> playersBySlot = Enumerable.Range(0, playerCount)
            .OrderBy(playerIndex => inputLayer.ResolvePlayerXInputSlot(playerIndex) < 0 ? int.MaxValue : inputLayer.ResolvePlayerXInputSlot(playerIndex))
            .ToList();

        foreach (int playerIndex in playersBySlot)
        {
            ListedController listedController = inputLayer.IsSteamInputSession
                ? listedControllers.ElementAtOrDefault(playerIndex)
                : listedControllers.FirstOrDefault(listed => !claimedNumbers.Contains(listed.Number) && listed.Name == ResolveFallbackDeviceName(playerIndex, inputLayer));

            if (listedController != null)
            {
                claimedNumbers.Add(listedController.Number);
                playerControllers[playerIndex] = listedController;
            }
        }

        foreach (int playerIndex in playersBySlot.Where(playerIndex => playerControllers[playerIndex] == null))
        {
            playerControllers[playerIndex] = listedControllers.FirstOrDefault(listed => !claimedNumbers.Contains(listed.Number));

            if (playerControllers[playerIndex] != null)
            {
                claimedNumbers.Add(playerControllers[playerIndex].Number);
            }
        }

        for (int playerIndex = 0; playerIndex < playerCount; playerIndex++)
        {
            ListedController listedController = playerControllers[playerIndex];

            if (listedController == null)
            {
                GD.Print($"[InputLayer] player {playerIndex + 1} (\"{ResolveFallbackDeviceName(playerIndex, inputLayer)}\") is not among {emulatorMetadata.Name}'s controllers.");
                continue;
            }

            string assignArgs = commands.AssignArgs.Replace("{number}", listedController.Number.ToString()).Replace("{port}", (playerIndex + 1).ToString());
            await RunControllerCommand(emulatorMetadata, emulatorInstallDirectory, executablePath, assignArgs);
            GD.Print($"[InputLayer] port {playerIndex + 1} = {emulatorMetadata.Name} controller {listedController.Number} \"{listedController.Name}\"");
        }

        WriteControllerPortStates(Path.Combine(emulatorInstallDirectory, commands.ConfigFileRelativePath), commands, playerCount);
    }

    private async Task<string> RunControllerCommand(EmulatorMeta emulatorMetadata, string emulatorInstallDirectory, string executablePath, string arguments)
    {
        ProcessStartInfo startInfo = BuildEmulatorStartInfo(executablePath, arguments, emulatorInstallDirectory, emulatorMetadata);
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        try
        {
            using Process commandProcess = Process.Start(startInfo);

            if (commandProcess == null)
            {
                return null;
            }

            Task<string> outputTask = commandProcess.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = commandProcess.StandardError.ReadToEndAsync();
            Task exitTask = commandProcess.WaitForExitAsync();

            if (await Task.WhenAny(exitTask, Task.Delay(ControllerCommandTimeoutMilliseconds)) != exitTask)
            {
                commandProcess.Kill(true);
                GD.PrintErr($"[InputLayer] {emulatorMetadata.Name} {arguments} did not finish within {ControllerCommandTimeoutMilliseconds / 1000} s.");
                return null;
            }

            string errorOutput = await errorTask;

            if (!string.IsNullOrWhiteSpace(errorOutput))
            {
                GD.Print($"[InputLayer] {emulatorMetadata.Name} {arguments}: {errorOutput.Trim()}");
            }

            return await outputTask;
        }
        catch (Exception commandFailure)
        {
            GD.PrintErr($"[InputLayer] could not run {emulatorMetadata.Name} {arguments}: {commandFailure.Message}");
            return null;
        }
    }

    private static void WriteControllerPortStates(string configFilePath, ControllerCommands commands, int playerCount)
    {
        if (!File.Exists(configFilePath))
        {
            return;
        }

        JsonNode configRoot = JsonNode.Parse(File.ReadAllText(configFilePath));
        JsonArray assignments = ResolveJsonArray(configRoot, commands.AssignmentJsonPath);
        JsonArray enabledPorts = ResolveJsonArray(configRoot, commands.EnabledJsonPath);

        for (int portIndex = 0; portIndex < commands.PortCount; portIndex++)
        {
            bool portHasPlayer = portIndex < playerCount;

            if (enabledPorts != null && portIndex < enabledPorts.Count)
            {
                enabledPorts[portIndex] = portHasPlayer;
            }

            if (!portHasPlayer && assignments != null && portIndex < assignments.Count)
            {
                assignments[portIndex] = null;
            }
        }

        File.WriteAllText(configFilePath, configRoot.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"[InputLayer] enabled {playerCount} of {commands.PortCount} port(s) in {Path.GetFileName(configFilePath)}");
    }

    private static JsonArray ResolveJsonArray(JsonNode root, string dottedPath)
    {
        JsonNode node = root;

        foreach (string segment in (dottedPath ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node?[segment];
        }

        return node as JsonArray;
    }
}
