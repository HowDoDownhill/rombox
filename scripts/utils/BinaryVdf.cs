using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public class BinaryVdfNode
{
    public const byte MapType = 0x00;
    public const byte StringType = 0x01;
    public const byte Int32Type = 0x02;
    public const byte EndType = 0x08;

    public byte Type;
    public string Key;
    public string StringValue;
    public byte[] RawValue;
    public List<BinaryVdfNode> Children = new List<BinaryVdfNode>();

    public static BinaryVdfNode Map(string key, IEnumerable<BinaryVdfNode> children = null)
    {
        return new BinaryVdfNode { Type = MapType, Key = key, Children = children?.ToList() ?? new List<BinaryVdfNode>() };
    }

    public static BinaryVdfNode String(string key, string value)
    {
        return new BinaryVdfNode { Type = StringType, Key = key, StringValue = value };
    }

    public static BinaryVdfNode Int32(string key, int value)
    {
        return new BinaryVdfNode { Type = Int32Type, Key = key, RawValue = BitConverter.GetBytes(value) };
    }

    public BinaryVdfNode Child(string key)
    {
        return Children.FirstOrDefault(child => string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase));
    }
}

public static class BinaryVdf
{
    public static List<BinaryVdfNode> Read(byte[] data)
    {
        int position = 0;
        return ReadChildren(data, ref position);
    }

    public static byte[] Write(IEnumerable<BinaryVdfNode> nodes)
    {
        using var stream = new MemoryStream();
        WriteChildren(stream, nodes);
        return stream.ToArray();
    }

    private static List<BinaryVdfNode> ReadChildren(byte[] data, ref int position)
    {
        var children = new List<BinaryVdfNode>();

        while (position < data.Length)
        {
            byte type = data[position++];

            if (type == BinaryVdfNode.EndType)
            {
                return children;
            }

            var node = new BinaryVdfNode { Type = type, Key = ReadUtf8String(data, ref position) };

            switch (type)
            {
                case BinaryVdfNode.MapType:
                    node.Children = ReadChildren(data, ref position);
                    break;
                case BinaryVdfNode.StringType:
                    node.StringValue = ReadUtf8String(data, ref position);
                    break;
                case 0x05:
                    node.RawValue = ReadWideStringBytes(data, ref position);
                    break;
                case 0x07:
                case 0x0A:
                    node.RawValue = ReadBytes(data, ref position, 8);
                    break;
                default:
                    node.RawValue = ReadBytes(data, ref position, 4);
                    break;
            }

            children.Add(node);
        }

        return children;
    }

    private static string ReadUtf8String(byte[] data, ref int position)
    {
        int end = Array.IndexOf(data, (byte)0, position);

        if (end < 0)
        {
            throw new InvalidDataException("unterminated string");
        }

        string value = Encoding.UTF8.GetString(data, position, end - position);
        position = end + 1;
        return value;
    }

    private static byte[] ReadWideStringBytes(byte[] data, ref int position)
    {
        int start = position;

        while (position + 1 < data.Length && (data[position] != 0 || data[position + 1] != 0))
        {
            position += 2;
        }

        position += 2;
        return data[start..Math.Min(position, data.Length)];
    }

    private static byte[] ReadBytes(byte[] data, ref int position, int count)
    {
        if (position + count > data.Length)
        {
            throw new InvalidDataException("value runs past the end of the file");
        }

        byte[] value = data[position..(position + count)];
        position += count;
        return value;
    }

    private static void WriteChildren(Stream stream, IEnumerable<BinaryVdfNode> nodes)
    {
        foreach (BinaryVdfNode node in nodes)
        {
            stream.WriteByte(node.Type);
            WriteUtf8String(stream, node.Key);

            switch (node.Type)
            {
                case BinaryVdfNode.MapType:
                    WriteChildren(stream, node.Children);
                    break;
                case BinaryVdfNode.StringType:
                    WriteUtf8String(stream, node.StringValue ?? "");
                    break;
                default:
                    stream.Write(node.RawValue ?? Array.Empty<byte>());
                    break;
            }
        }

        stream.WriteByte(BinaryVdfNode.EndType);
    }

    private static void WriteUtf8String(Stream stream, string value)
    {
        stream.Write(Encoding.UTF8.GetBytes(value ?? ""));
        stream.WriteByte(0);
    }
}
