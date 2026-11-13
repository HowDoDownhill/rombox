using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

// Linux: finds the X11 window a running program opened, so the stream can capture that window
// alone. Windows opened under XWayland count. Asked through libxcb, which every desktop with X11
// or XWayland has; it reports a vanished window as an ordinary failed request.
public static class X11Windows
{
    public struct Found
    {
        public long WindowId;
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Cookie
    {
        public uint Sequence;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenIterator
    {
        public IntPtr Screen;
        public int Remaining;
        public int Index;
    }

    private const string Xcb = "libxcb.so.1";
    private const uint AnyType = 0;

    [DllImport(Xcb)] private static extern IntPtr xcb_connect(string display, IntPtr screen);
    [DllImport(Xcb)] private static extern int xcb_connection_has_error(IntPtr connection);
    [DllImport(Xcb)] private static extern void xcb_disconnect(IntPtr connection);
    [DllImport(Xcb)] private static extern IntPtr xcb_get_setup(IntPtr connection);
    [DllImport(Xcb)] private static extern ScreenIterator xcb_setup_roots_iterator(IntPtr setup);
    [DllImport(Xcb)] private static extern Cookie xcb_intern_atom(IntPtr connection, byte onlyIfExists, ushort nameLength, string name);
    [DllImport(Xcb)] private static extern IntPtr xcb_intern_atom_reply(IntPtr connection, Cookie cookie, IntPtr error);
    [DllImport(Xcb)] private static extern Cookie xcb_get_property(IntPtr connection, byte delete, uint window, uint property, uint type, uint offset, uint length);
    [DllImport(Xcb)] private static extern IntPtr xcb_get_property_reply(IntPtr connection, Cookie cookie, IntPtr error);
    [DllImport(Xcb)] private static extern Cookie xcb_get_geometry(IntPtr connection, uint drawable);
    [DllImport(Xcb)] private static extern IntPtr xcb_get_geometry_reply(IntPtr connection, Cookie cookie, IntPtr error);
    [DllImport("libc")] private static extern void free(IntPtr memory);

    // Why the last search found nothing, for the log.
    public static string LastProblem { get; private set; }

    // The largest window owned by the process or by a program it started, or null. Emulators
    // packed as an AppImage run as a child of the program RomBox launched.
    public static Found? FindForProcess(int processId)
    {
        try
        {
            HashSet<int> family = ProcessFamily(processId);
            LastProblem = $"none of the X display's windows belongs to process {processId} or the {family.Count - 1} program(s) it started";
            return Find(family);
        }
        catch (Exception failure)
        {
            // No X display, or libxcb is missing.
            LastProblem = failure.Message;
            return null;
        }
    }

    private static Found? Find(HashSet<int> family)
    {
        IntPtr connection = xcb_connect(null, IntPtr.Zero);

        if (connection == IntPtr.Zero)
        {
            LastProblem = "the X display did not answer";
            return null;
        }

        try
        {
            if (xcb_connection_has_error(connection) != 0)
            {
                LastProblem = "the X display refused the connection";
                return null;
            }

            ScreenIterator screens = xcb_setup_roots_iterator(xcb_get_setup(connection));

            if (screens.Remaining <= 0 || screens.Screen == IntPtr.Zero)
            {
                return null;
            }

            uint root = (uint)Marshal.ReadInt32(screens.Screen);
            uint clientList = Atom(connection, "_NET_CLIENT_LIST");
            uint windowPid = Atom(connection, "_NET_WM_PID");

            if (clientList == 0 || windowPid == 0)
            {
                LastProblem = "the desktop keeps no list of windows on the X display";
                return null;
            }

            Found? best = null;

            foreach (uint window in ReadNumbers(connection, root, clientList, 4096))
            {
                uint[] owner = ReadNumbers(connection, window, windowPid, 1);

                if (owner.Length == 0 || !family.Contains((int)owner[0]))
                {
                    continue;
                }

                IntPtr geometry = xcb_get_geometry_reply(connection, xcb_get_geometry(connection, window), IntPtr.Zero);

                if (geometry == IntPtr.Zero)
                {
                    continue;
                }

                // In the reply the width and height are 16-bit numbers at bytes 16 and 18.
                int width = (ushort)Marshal.ReadInt16(geometry, 16);
                int height = (ushort)Marshal.ReadInt16(geometry, 18);
                free(geometry);

                if (best == null || width * height > best.Value.Width * best.Value.Height)
                {
                    best = new Found { WindowId = window, Width = width, Height = height };
                }
            }

            return best;
        }
        finally
        {
            xcb_disconnect(connection);
        }
    }

    private static uint Atom(IntPtr connection, string name)
    {
        IntPtr reply = xcb_intern_atom_reply(connection, xcb_intern_atom(connection, 1, (ushort)name.Length, name), IntPtr.Zero);

        if (reply == IntPtr.Zero)
        {
            return 0;
        }

        // The atom is the 32-bit number at byte 8 of the reply.
        uint atom = (uint)Marshal.ReadInt32(reply, 8);
        free(reply);
        return atom;
    }

    // A property made of 32-bit numbers, such as a list of window ids or a process id.
    private static uint[] ReadNumbers(IntPtr connection, uint window, uint property, uint mostNumbers)
    {
        IntPtr reply = xcb_get_property_reply(connection, xcb_get_property(connection, 0, window, property, AnyType, 0, mostNumbers), IntPtr.Zero);

        if (reply == IntPtr.Zero)
        {
            return Array.Empty<uint>();
        }

        try
        {
            // Byte 1 is the size of one value in bits, bytes 16 to 19 the count; values start at byte 32.
            if (Marshal.ReadByte(reply, 1) != 32)
            {
                return Array.Empty<uint>();
            }

            int count = Marshal.ReadInt32(reply, 16);
            uint[] numbers = new uint[count];

            for (int index = 0; index < count; index++)
            {
                numbers[index] = (uint)Marshal.ReadInt32(reply, 32 + index * 4);
            }

            return numbers;
        }
        finally
        {
            free(reply);
        }
    }

    // The process and every program started by it, directly or through others, read from /proc.
    public static HashSet<int> ProcessFamily(int processId)
    {
        var parents = new Dictionary<int, int>();

        foreach (string directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out int id))
            {
                continue;
            }

            try
            {
                // "pid (name) state parent ...". The name can hold spaces and brackets, so the
                // fields are counted from the last closing bracket.
                string stat = File.ReadAllText(Path.Combine(directory, "stat"));
                string[] fields = stat.Substring(stat.LastIndexOf(')') + 2).Split(' ');
                parents[id] = int.Parse(fields[1]);
            }
            catch (Exception)
            {
                // The process ended while being read.
            }
        }

        var family = new HashSet<int> { processId };
        bool grew = true;

        while (grew)
        {
            grew = false;

            foreach (KeyValuePair<int, int> entry in parents)
            {
                if (family.Contains(entry.Value) && family.Add(entry.Key))
                {
                    grew = true;
                }
            }
        }

        return family;
    }
}
