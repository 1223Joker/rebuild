using System.Buffers.Binary;
using Rebuild.Sim.Core;

namespace Rebuild.Sim.Serialization;

/// <summary>
/// Canonical binary form: explicit field order, little-endian, no padding, no reflection.
/// Used for saves, the state hash, reconnect snapshots and desync dumps (docs/01-architecture.md §8).
/// </summary>
public sealed class CanonicalWriter
{
    private byte[] _buffer;
    private int _length;

    public CanonicalWriter(int capacity = 256) => _buffer = new byte[System.Math.Max(capacity, 16)];

    public int Length => _length;

    public System.ReadOnlySpan<byte> WrittenSpan => new(_buffer, 0, _length);

    public byte[] ToArray() => WrittenSpan.ToArray();

    public void Clear() => _length = 0;

    private System.Span<byte> Reserve(int count)
    {
        if (_length + count > _buffer.Length)
        {
            int size = _buffer.Length * 2;
            while (size < _length + count) size *= 2;
            System.Array.Resize(ref _buffer, size);
        }
        var span = new System.Span<byte>(_buffer, _length, count);
        _length += count;
        return span;
    }

    public void WriteByte(byte v) => Reserve(1)[0] = v;
    public void WriteBool(bool v) => WriteByte(v ? (byte)1 : (byte)0);
    public void WriteInt16(short v) => BinaryPrimitives.WriteInt16LittleEndian(Reserve(2), v);
    public void WriteUInt16(ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(Reserve(2), v);
    public void WriteInt32(int v) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), v);
    public void WriteUInt32(uint v) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), v);
    public void WriteInt64(long v) => BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), v);
    public void WriteUInt64(ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(8), v);
    public void WriteFix(Fix v) => WriteInt64(v.Raw);

    /// <summary>Raw bytes without a length prefix.</summary>
    public void WriteRaw(System.ReadOnlySpan<byte> bytes) => bytes.CopyTo(Reserve(bytes.Length));

    /// <summary>Length-prefixed (int32) byte block.</summary>
    public void WriteBytes(System.ReadOnlySpan<byte> bytes)
    {
        WriteInt32(bytes.Length);
        WriteRaw(bytes);
    }

    /// <summary>Length-prefixed UTF-8 string.</summary>
    public void WriteString(string s)
    {
        int count = System.Text.Encoding.UTF8.GetByteCount(s);
        WriteInt32(count);
        System.Text.Encoding.UTF8.GetBytes(s, Reserve(count));
    }
}
