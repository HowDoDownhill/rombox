using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public class UsbBluetoothAdapter
{
    public string Name { get; set; }

    public int VendorId { get; set; }

    public int ProductId { get; set; }

    public string Identifier => UsbBluetoothAdapters.FormatIdentifier(VendorId, ProductId);
}

public static class UsbBluetoothAdapters
{
    private const string LinuxUsbDevicesPath = "/sys/bus/usb/devices";
    private const string WindowsUsbEnumKeyPath = @"SYSTEM\CurrentControlSet\Enum\USB";
    private const string BluetoothInterfaceClass = "e0";
    private const string WindowsBluetoothCompatibleId = "Class_E0&SubClass_01&Prot_01";

    private static readonly string[] WindowsPassthroughDrivers = { "WINUSB", "libusbK", "libusb0" };
    private static readonly Regex WindowsHardwareIdPattern = new Regex(@"^VID_([0-9A-F]{4})&PID_([0-9A-F]{4})$", RegexOptions.IgnoreCase);
    private static readonly Regex IdentifierPattern = new Regex(@"^([0-9a-f]{4}):([0-9a-f]{4})$", RegexOptions.IgnoreCase);

    public static string FormatIdentifier(int vendorId, int productId) => $"{vendorId:x4}:{productId:x4}";

    public static bool TryParseIdentifier(string identifier, out int vendorId, out int productId)
    {
        vendorId = -1;
        productId = -1;
        Match identifierMatch = IdentifierPattern.Match(identifier ?? "");

        if (!identifierMatch.Success)
        {
            return false;
        }

        vendorId = int.Parse(identifierMatch.Groups[1].Value, NumberStyles.HexNumber);
        productId = int.Parse(identifierMatch.Groups[2].Value, NumberStyles.HexNumber);
        return true;
    }

    public static List<UsbBluetoothAdapter> List()
    {
        var adapters = new List<UsbBluetoothAdapter>();

        try
        {
            if (OperatingSystem.IsWindows())
            {
                adapters = ListOnWindows();
            }

            else if (OperatingSystem.IsLinux())
            {
                adapters = ListOnLinux();
            }
        }

        catch (Exception exception)
        {
            Godot.GD.PrintErr($"Could not list USB Bluetooth adapters: {exception.Message}");
        }

        return adapters.GroupBy(adapter => adapter.Identifier).Select(sameIdentifier => sameIdentifier.First()).ToList();
    }

    private static List<UsbBluetoothAdapter> ListOnLinux()
    {
        var adapters = new List<UsbBluetoothAdapter>();

        if (!Directory.Exists(LinuxUsbDevicesPath))
        {
            return adapters;
        }

        foreach (string devicePath in Directory.GetDirectories(LinuxUsbDevicesPath))
        {
            string vendorText = ReadSysfsValue(devicePath, "idVendor");
            string productText = ReadSysfsValue(devicePath, "idProduct");

            if (!int.TryParse(vendorText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int vendorId)
                || !int.TryParse(productText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int productId))
            {
                continue;
            }

            bool isBluetooth = ReadSysfsValue(devicePath, "bDeviceClass") == BluetoothInterfaceClass
                || Directory.GetDirectories(devicePath).Any(interfacePath => ReadSysfsValue(interfacePath, "bInterfaceClass") == BluetoothInterfaceClass);

            if (!isBluetooth)
            {
                continue;
            }

            string productName = ReadSysfsValue(devicePath, "product");
            string manufacturerName = ReadSysfsValue(devicePath, "manufacturer");

            adapters.Add(new UsbBluetoothAdapter
            {
                Name = string.Join(" ", new[] { manufacturerName, productName }.Where(namePart => !string.IsNullOrEmpty(namePart))),
                VendorId = vendorId,
                ProductId = productId
            });
        }

        return adapters;
    }

    private static string ReadSysfsValue(string directoryPath, string fileName)
    {
        string filePath = Path.Combine(directoryPath, fileName);

        try
        {
            return File.Exists(filePath) ? File.ReadAllText(filePath).Trim() : "";
        }

        catch
        {
            return "";
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<UsbBluetoothAdapter> ListOnWindows()
    {
        var adapters = new List<UsbBluetoothAdapter>();
        using var usbEnumKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(WindowsUsbEnumKeyPath);

        if (usbEnumKey == null)
        {
            return adapters;
        }

        foreach (string hardwareId in usbEnumKey.GetSubKeyNames())
        {
            Match hardwareIdMatch = WindowsHardwareIdPattern.Match(hardwareId);

            if (!hardwareIdMatch.Success)
            {
                continue;
            }

            using var hardwareKey = usbEnumKey.OpenSubKey(hardwareId);

            foreach (string instanceName in hardwareKey?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var instanceKey = hardwareKey.OpenSubKey(instanceName);

                if (instanceKey == null)
                {
                    continue;
                }

                string[] compatibleIds = instanceKey.GetValue("CompatibleIDs") as string[] ?? Array.Empty<string>();
                string driverService = instanceKey.GetValue("Service") as string ?? "";

                bool isBluetooth = compatibleIds.Any(compatibleId => compatibleId.Contains(WindowsBluetoothCompatibleId, StringComparison.OrdinalIgnoreCase));
                bool usesPassthroughDriver = WindowsPassthroughDrivers.Any(driverName => string.Equals(driverName, driverService, StringComparison.OrdinalIgnoreCase));

                if (!isBluetooth && !usesPassthroughDriver)
                {
                    continue;
                }

                string deviceName = instanceKey.GetValue("FriendlyName") as string ?? instanceKey.GetValue("DeviceDesc") as string ?? "";

                adapters.Add(new UsbBluetoothAdapter
                {
                    Name = deviceName.Substring(deviceName.LastIndexOf(';') + 1).Trim(),
                    VendorId = int.Parse(hardwareIdMatch.Groups[1].Value, NumberStyles.HexNumber),
                    ProductId = int.Parse(hardwareIdMatch.Groups[2].Value, NumberStyles.HexNumber)
                });
            }
        }

        return adapters;
    }
}
