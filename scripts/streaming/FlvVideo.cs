using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

// FFmpeg writes the encoded stream as FLV because every FLV tag states its size up front. That
// lets the host send a frame the moment it is written. A raw H.264 stream only shows where a
// frame ends when the next one starts, which costs a frame of delay.
public sealed class FlvVideoReader
{
    private const byte AudioTag = 8;
    private const byte VideoTag = 9;
    private const byte ScriptTag = 18;
    private const int AvcCodecId = 7;

    private readonly Stream input;
    private readonly byte[] tagHeader = new byte[11];
    private readonly byte[] sizeField = new byte[4];

    public int Width { get; private set; }
    public int Height { get; private set; }
    public double FrameRate { get; private set; }

    public event Action<byte[]> DecoderSetupRead;
    public event Action<byte[], bool> FrameRead;
    public event Action MetadataRead;

    public FlvVideoReader(Stream input)
    {
        this.input = input;
    }

    // Reads until the stream ends. Returns false if the stream is not FLV.
    public bool ReadAll()
    {
        byte[] fileHeader = new byte[9];

        if (!ReadExactly(fileHeader, 9) || fileHeader[0] != 'F' || fileHeader[1] != 'L' || fileHeader[2] != 'V')
        {
            return false;
        }

        int dataOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(fileHeader.AsSpan(5));

        if (dataOffset > 9 && !Skip(dataOffset - 9))
        {
            return true;
        }

        if (!ReadExactly(sizeField, 4))
        {
            return true;
        }

        while (ReadExactly(tagHeader, 11))
        {
            byte tagType = (byte)(tagHeader[0] & 0x1F);
            int dataSize = (tagHeader[1] << 16) | (tagHeader[2] << 8) | tagHeader[3];
            byte[] data = new byte[dataSize];

            if (!ReadExactly(data, dataSize) || !ReadExactly(sizeField, 4))
            {
                return true;
            }

            if (tagType == VideoTag)
            {
                HandleVideoTag(data);
            }
            else if (tagType == ScriptTag)
            {
                HandleScriptTag(data);
            }
        }

        return true;
    }

    private void HandleVideoTag(byte[] data)
    {
        if (data.Length < 5 || (data[0] & 0x0F) != AvcCodecId)
        {
            return;
        }

        bool isKeyframe = (data[0] >> 4) == 1;
        byte packetType = data[1];
        byte[] payload = data.AsSpan(5).ToArray();

        if (packetType == 0)
        {
            DecoderSetupRead?.Invoke(payload);
        }
        else if (packetType == 1 && payload.Length > 0)
        {
            FrameRead?.Invoke(payload, isKeyframe);
        }
    }

    // onMetaData is an AMF0 name followed by an ECMA array of properties.
    private void HandleScriptTag(byte[] data)
    {
        try
        {
            int position = 0;

            if (data[position++] != 0x02 || ReadAmfString(data, ref position) != "onMetaData")
            {
                return;
            }

            byte containerType = data[position++];

            if (containerType == 0x08)
            {
                position += 4;
            }
            else if (containerType != 0x03)
            {
                return;
            }

            while (position + 3 <= data.Length)
            {
                string name = ReadAmfString(data, ref position);

                if (name.Length == 0 && data[position] == 0x09)
                {
                    break;
                }

                byte valueType = data[position++];

                switch (valueType)
                {
                    case 0x00:
                        double number = BinaryPrimitives.ReadDoubleBigEndian(data.AsSpan(position));
                        position += 8;
                        if (name == "width") Width = (int)number;
                        else if (name == "height") Height = (int)number;
                        else if (name == "framerate") FrameRate = number;
                        break;
                    case 0x01:
                        position += 1;
                        break;
                    case 0x02:
                        ReadAmfString(data, ref position);
                        break;
                    default:
                        // Any other value type ends the walk. Size and frame rate come first.
                        MetadataRead?.Invoke();
                        return;
                }
            }
        }
        catch (Exception exception) when (exception is IndexOutOfRangeException || exception is ArgumentOutOfRangeException)
        {
        }

        MetadataRead?.Invoke();
    }

    private static string ReadAmfString(byte[] data, ref int position)
    {
        int length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position));
        position += 2;
        string text = Encoding.UTF8.GetString(data, position, length);
        position += length;
        return text;
    }

    private bool ReadExactly(byte[] buffer, int count)
    {
        int offset = 0;

        while (offset < count)
        {
            int read = input.Read(buffer, offset, count - offset);

            if (read <= 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private bool Skip(int count)
    {
        byte[] scratch = new byte[count];
        return ReadExactly(scratch, count);
    }
}
