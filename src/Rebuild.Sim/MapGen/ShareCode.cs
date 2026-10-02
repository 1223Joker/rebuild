using System.IO;
using System.Text;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;

namespace Rebuild.Sim.MapGen;

/// <summary>
/// Copy-paste string for a <see cref="MapSpec"/> (docs/03-mapgen.md §2): canonical spec bytes + 16-bit checksum,
/// Crockford base32 (https://www.crockford.com/base32.html) in groups of 5, prefixed "RB-".
/// Decoding is case-insensitive, ignores '-' and spaces (also in the prefix) and reads I/L as 1 and O as 0.
/// </summary>
public static class ShareCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const string Prefix = "RB-";

    public static string Encode(MapSpec spec)
    {
        var w = new CanonicalWriter(32);
        spec.WriteTo(w);
        ushort check = Checksum(w.WrittenSpan);
        w.WriteUInt16(check);
        var bytes = w.WrittenSpan;

        var sb = new StringBuilder(Prefix);
        int buffer = 0, bits = 0, chars = 0;
        void Emit(int value)
        {
            if (chars > 0 && chars % 5 == 0) sb.Append('-');
            sb.Append(Alphabet[value]);
            chars++;
        }
        foreach (byte b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                Emit((buffer >> bits) & 31);
            }
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0) Emit((buffer << (5 - bits)) & 31);
        return sb.ToString();
    }

    public static MapSpec Decode(string code)
    {
        string s = code.Trim();
        // The payload starts with the format byte (< 8), i.e. with '0', so a leading "RB" is always the prefix.
        if (s.StartsWith("RB", System.StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
        var bytes = new System.Collections.Generic.List<byte>(32);
        int buffer = 0, bits = 0;
        foreach (char raw in s)
        {
            if (raw == '-' || raw == ' ') continue;
            int value = ValueOf(raw);
            if (value < 0) throw new InvalidDataException($"Invalid character '{raw}' in share code");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }
        if (bytes.Count < 3) throw new InvalidDataException("Share code too short");
        var data = bytes.ToArray();
        int body = data.Length - 2;
        ushort expected = (ushort)(data[body] | (data[body + 1] << 8));
        if (Checksum(new System.ReadOnlySpan<byte>(data, 0, body)) != expected)
            throw new InvalidDataException("Share code checksum mismatch (typo?)");
        var r = new CanonicalReader(data, 0, body);
        var spec = MapSpec.ReadFrom(r);
        if (!r.AtEnd) throw new InvalidDataException("Trailing data in share code");
        return spec;
    }

    private static ushort Checksum(System.ReadOnlySpan<byte> bytes) => (ushort)StateHash.Of(bytes);

    private static int ValueOf(char c)
    {
        c = char.ToUpperInvariant(c);
        if (c == 'I' || c == 'L') return 1;
        if (c == 'O') return 0;
        return Alphabet.IndexOf(c);
    }
}
