using Nfs.Abstractions;
using Nfs.Xdr;

using Xunit;

namespace Nfs.Protocol.V2.Tests;

public sealed class Nfs2WireTests
{
    [Fact]
    public void Handle_RoundTrips()
    {
        var handle = new Nfs2Handle { Data = MakeHandle(0x01020304) };

        Nfs2Handle decoded = RoundTrip(handle, Nfs2Handle.ReadFrom);

        Assert.Equal(handle.Data, decoded.Data);
    }

    [Fact]
    public void FileAttributes_RoundTrip()
    {
        var attributes = new Nfs2FileAttributes
        {
            Type = NfsFileType.Regular,
            Mode = 0x1A4,
            LinkCount = 1,
            Uid = 1000,
            Gid = 1000,
            Size = 4096,
            BlockSize = 512,
            Rdev = 0,
            Blocks = 8,
            FileSystemId = 7,
            FileId = 42,
            AccessTime = new Nfs2Time { Seconds = 100, MicroSeconds = 200 },
            ModifyTime = new Nfs2Time { Seconds = 300, MicroSeconds = 400 },
            ChangeTime = new Nfs2Time { Seconds = 500, MicroSeconds = 600 },
        };

        Nfs2FileAttributes decoded = RoundTrip(attributes, Nfs2FileAttributes.ReadFrom);

        Assert.Equal(NfsFileType.Regular, decoded.Type);
        Assert.Equal(4096u, decoded.Size);
        Assert.Equal(42u, decoded.FileId);
        Assert.Equal(600u, decoded.ChangeTime.MicroSeconds);
    }

    [Fact]
    public void AttrStat_Failure_OmitsAttributes()
    {
        Nfs2AttrStat failure = Nfs2AttrStat.Failure(NfsStatus.StaleHandle);

        Nfs2AttrStat decoded = RoundTrip(failure, Nfs2AttrStat.ReadFrom);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(NfsStatus.StaleHandle, decoded.Status);
    }

    [Fact]
    public void DirOpResult_Success_CarriesHandleAndAttributes()
    {
        var result = Nfs2DirOpResult.Success(
            new Nfs2Handle { Data = MakeHandle(9) },
            new Nfs2FileAttributes { Type = NfsFileType.Directory, FileId = 9 });

        Nfs2DirOpResult decoded = RoundTrip(result, Nfs2DirOpResult.ReadFrom);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(NfsFileType.Directory, decoded.Attributes.Type);
        Assert.Equal(9u, decoded.Attributes.FileId);
    }

    [Fact]
    public void ReadDirResult_RoundTripsEntries()
    {
        var result = Nfs2ReadDirResult.Success(
            [
                new Nfs2DirEntry(1, "a.txt", [0, 0, 0, 1]),
                new Nfs2DirEntry(2, "b.txt", [0, 0, 0, 2]),
            ],
            eof: true);

        Nfs2ReadDirResult decoded = RoundTrip(result, Nfs2ReadDirResult.ReadFrom);

        Assert.True(decoded.Eof);
        Assert.Equal(["a.txt", "b.txt"], decoded.Entries.Select(e => e.Name).ToArray());
        Assert.Equal(2u, decoded.Entries[1].FileId);
    }

    private static byte[] MakeHandle(uint id)
    {
        byte[] data = new byte[Nfs2.HandleSize];
        data[0] = (byte)(id >> 24);
        data[1] = (byte)(id >> 16);
        data[2] = (byte)(id >> 8);
        data[3] = (byte)id;
        return data;
    }

    private static T RoundTrip<T>(T value, XdrReadFunc<T> read)
        where T : IXdrSerializable<T>
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        value.WriteTo(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        return read(ref reader);
    }

    private delegate T XdrReadFunc<T>(ref XdrReader reader);
}
