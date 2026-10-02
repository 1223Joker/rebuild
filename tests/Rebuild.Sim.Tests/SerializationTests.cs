using System.IO;
using System.Linq;
using FsCheck.Xunit;
using Rebuild.Sim.Commands;
using Rebuild.Sim.Core;
using Rebuild.Sim.Match;
using Rebuild.Sim.Serialization;
using Rebuild.Tools;
using Xunit;

namespace Rebuild.Sim.Tests;

public class SerializationTests
{
    [Property(MaxTest = 500)]
    public bool Primitives_round_trip(byte b, bool f, short s, ushort us, int i, uint ui, long l, ulong ul, string? text)
    {
        text ??= "";
        var w = new CanonicalWriter(1);
        w.WriteByte(b); w.WriteBool(f); w.WriteInt16(s); w.WriteUInt16(us); w.WriteInt32(i);
        w.WriteUInt32(ui); w.WriteInt64(l); w.WriteUInt64(ul); w.WriteFix(Fix.FromRaw(l)); w.WriteString(text);
        w.WriteBytes(new byte[] { 1, 2, 3 });
        var r = new CanonicalReader(w.ToArray());
        // Strings with lone surrogates are not valid UTF-8 and do not round-trip; ignore those inputs.
        bool validText = System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(text)) == text;
        return r.ReadByte() == b && r.ReadBool() == f && r.ReadInt16() == s && r.ReadUInt16() == us
            && r.ReadInt32() == i && r.ReadUInt32() == ui && r.ReadInt64() == l && r.ReadUInt64() == ul
            && r.ReadFix().Raw == l && (r.ReadString() == text || !validText)
            && r.ReadBytes().SequenceEqual(new byte[] { 1, 2, 3 }) && r.AtEnd;
    }

    [Fact]
    public void Layout_is_little_endian_without_padding()
    {
        var w = new CanonicalWriter();
        w.WriteUInt16(0x0102);
        w.WriteInt32(-2);
        Assert.Equal(new byte[] { 0x02, 0x01, 0xFE, 0xFF, 0xFF, 0xFF }, w.ToArray());
    }

    [Fact]
    public void Truncated_data_throws()
    {
        var r = new CanonicalReader(new byte[] { 1, 2, 3 });
        Assert.Throws<InvalidDataException>(() => r.ReadInt32());
    }

    [Fact]
    public void TurnBundle_sorts_by_slot_then_seq_and_drops_duplicates()
    {
        var bundle = new TurnBundle(5, new[]
        {
            new Command(CommandType.Move, 2, 0, 1),
            new Command(CommandType.Stop, 0, 0, 9),
            new Command(CommandType.Hold, 2, 0, 0),
            new Command(CommandType.Attack, 2, 0, 1), // duplicate (slot 2, seq 1) → dropped
            MetaCommands.Pause(0),
        });
        Assert.Equal(new[] { CommandType.Stop, CommandType.Hold, CommandType.Move, CommandType.Pause },
            bundle.Commands.Select(c => c.Type).ToArray());
        Assert.All(bundle.Commands, c => Assert.Equal(5u, c.TargetTurn));
    }

    [Fact]
    public void CommandLog_round_trips()
    {
        var log = SampleLogs.MetaScript(seed: 9, turns: 200);
        var bytes = log.ToBytes();
        var copy = CommandLog.FromBytes(bytes);
        Assert.Equal(log.Version, copy.Version);
        Assert.Equal(log.Bundles.Count, copy.Bundles.Count);
        Assert.Equal(bytes, copy.ToBytes());
    }

    [Fact]
    public void CommandLog_streamed_form_equals_whole_form()
    {
        var log = SampleLogs.MetaScript(seed: 4, turns: 50);
        var streamed = CommandLog.EncodeHeader(log.Version, log.Setup)
            .Concat(log.Bundles.SelectMany(CommandLog.EncodeBundle)).ToArray();
        Assert.Equal(log.ToBytes(), streamed);
    }

    [Fact]
    public void CommandLog_rejects_gaps()
    {
        var log = new CommandLog(GameVersion.Current, SampleLogs.MetaScript(1, 1).Setup);
        Assert.Throws<System.ArgumentException>(() => log.Append(TurnBundle.Empty(1)));
    }
}
