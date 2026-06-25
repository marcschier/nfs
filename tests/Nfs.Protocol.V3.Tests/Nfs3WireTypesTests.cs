using System.Buffers;

using Nfs.Abstractions;
using Nfs.Xdr;

using Xunit;

namespace Nfs.Protocol.V3.Tests;

public sealed class Nfs3WireTypesTests
{
    [Fact]
    public void Time_ProducesExpectedWireBytes()
    {
        var time = new Nfs3Time { Seconds = 1, Nanoseconds = 2 };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        time.WriteTo(ref writer);

        Assert.Equal(Hex("00000001" + "00000002"), buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void FileAttributes_RoundTrip()
    {
        var attributes = new Nfs3FileAttributes
        {
            Type = NfsFileType.Regular,
            Mode = 0x1A4, // 0644
            LinkCount = 1,
            Uid = 1000,
            Gid = 1000,
            Size = 123456,
            Used = 124928,
            Rdev = new Nfs3SpecData { Major = 0, Minor = 0 },
            FileSystemId = 0x0102030405060708,
            FileId = 42,
            AccessTime = new Nfs3Time { Seconds = 100, Nanoseconds = 1 },
            ModifyTime = new Nfs3Time { Seconds = 200, Nanoseconds = 2 },
            ChangeTime = new Nfs3Time { Seconds = 300, Nanoseconds = 3 },
        };

        Nfs3FileAttributes decoded = RoundTrip(attributes);
        Assert.Equal(attributes, decoded);
    }

    [Fact]
    public void WccData_WithBothPresent_RoundTrips()
    {
        var value = new Nfs3WccData
        {
            Before = new Nfs3WccAttributes
            {
                Size = 10,
                ModifyTime = new Nfs3Time { Seconds = 1, Nanoseconds = 0 },
                ChangeTime = new Nfs3Time { Seconds = 2, Nanoseconds = 0 },
            },
            After = new Nfs3FileAttributes { Type = NfsFileType.Directory, Mode = 0x1FF, Size = 4096 },
        };

        Nfs3WccData decoded = RoundTrip(value);

        Assert.Equal(value.Before, decoded.Before);
        Assert.Equal(value.After, decoded.After);
    }

    [Fact]
    public void WccData_AllAbsent_ProducesTwoFalseFlags()
    {
        var value = default(Nfs3WccData);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        value.WriteTo(ref writer);

        // pre_op_attr absent (false) + post_op_attr absent (false)
        Assert.Equal(Hex("00000000" + "00000000"), buffer.WrittenSpan.ToArray());

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs3WccData decoded = Nfs3WccData.ReadFrom(ref reader);
        Assert.Null(decoded.Before);
        Assert.Null(decoded.After);
    }

    [Fact]
    public void Handle_RoundTrips()
    {
        var handle = new Nfs3Handle { Data = [0xDE, 0xAD, 0xBE, 0xEF, 0x01] };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        handle.WriteTo(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs3Handle decoded = Nfs3Handle.ReadFrom(ref reader);

        Assert.Equal(handle.Data, decoded.Data);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void DirOpArgs_RoundTrips()
    {
        var args = new Nfs3DirOpArgs
        {
            Directory = new Nfs3Handle { Data = [1, 2, 3, 4] },
            Name = "file.txt",
        };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        args.WriteTo(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs3DirOpArgs decoded = Nfs3DirOpArgs.ReadFrom(ref reader);

        Assert.Equal(args.Directory.Data, decoded.Directory.Data);
        Assert.Equal("file.txt", decoded.Name);
        Assert.Equal(0, reader.Remaining);
    }

    private static T RoundTrip<T>(T value)
        where T : IXdrSerializable<T>
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        value.WriteTo(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        return T.ReadFrom(ref reader);
    }

    private static byte[] Hex(string hex) =>
        Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));
}
