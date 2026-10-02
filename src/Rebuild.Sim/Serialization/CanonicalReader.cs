using System.Buffers.Binary;
using System.IO;
using Rebuild.Sim.Core;

namespace Rebuild.Sim.Serialization;

/// <summary>Reads the format written by <see cref="CanonicalWriter"/>; throws InvalidDataException on truncation.</summary>
public sealed class CanonicalReader
{
    private readonly byte[] _buffer;
    private readonly int _end;
    private int _pos;

    public CanonicalReader(byte[] buffer) : this(buffer, 0, buffer.Length)
    {
    }

    public CanonicalReader(byte[] buffer, int offset, int count)
    {
        _buffer = buffer;
        _pos = offset;
        _end = offset + count;
    }

    public int Position => _pos;
    public int Remaining => _end - _pos;
    public bool AtEnd => _pos >= _end;

    private System.ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || _pos + count > _end) throw new InvalidDataException("Unexpected end of data");
        var span = new System.ReadOnlySpan<byte>(_buffer, _pos, count);
        _pos += count;
        return span;
    }

    public byte ReadByte() => Take(1)[0];

    public bool ReadBool()
    {
        byte b = ReadByte();
        if (b > 1) throw new InvalidDataException("Invalid bool");
        return b == 1;
    }

    public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
    public Fix ReadFix() => Fix.FromRaw(ReadInt64());

    public byte[] ReadRaw(int count) => Take(count).ToArray();

    public byte[] ReadBytes() => Take(ReadInt32()).ToArray();

    public string ReadString() => System.Text.Encoding.UTF8.GetString(Take(ReadInt32()));
}
