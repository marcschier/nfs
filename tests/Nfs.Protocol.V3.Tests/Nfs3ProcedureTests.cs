using System.Buffers;

using Nfs.Abstractions;
using Nfs.Xdr;

using Xunit;

namespace Nfs.Protocol.V3.Tests;

public sealed class Nfs3ProcedureTests
{
    [Fact]
    public void GetAttrResult_Success_RoundTrips()
    {
        var result = Nfs3GetAttrResult.Success(new Nfs3FileAttributes
        {
            Type = NfsFileType.Regular,
            Mode = 0x1A4,
            Size = 2048,
            FileId = 99,
        });

        Nfs3GetAttrResult decoded = RoundTrip(result);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(result.Attributes, decoded.Attributes);
    }

    [Fact]
    public void GetAttrResult_Failure_EncodesOnlyTheStatus()
    {
        var result = Nfs3GetAttrResult.Failure(NfsStatus.StaleHandle);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        result.WriteTo(ref writer);

        // STALE = 70 = 0x46; the union has no data on failure.
        Assert.Equal(Convert.FromHexString("00000046"), buffer.WrittenSpan.ToArray());

        var reader = new XdrReader(buffer.WrittenSpan);
        Nfs3GetAttrResult decoded = Nfs3GetAttrResult.ReadFrom(ref reader);
        Assert.False(decoded.IsSuccess);
        Assert.Equal(NfsStatus.StaleHandle, decoded.Status);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void LookupResult_Success_RoundTrips()
    {
        var result = Nfs3LookupResult.Success(new Nfs3LookupResultOk
        {
            Handle = new Nfs3Handle { Data = [1, 2, 3, 4] },
            ObjectAttributes = new Nfs3FileAttributes { Type = NfsFileType.Regular, FileId = 7 },
            DirectoryAttributes = new Nfs3FileAttributes { Type = NfsFileType.Directory, FileId = 2 },
        });

        Nfs3LookupResult decoded = RoundTrip(result);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(result.Ok.Handle.Data, decoded.Ok.Handle.Data);
        Assert.Equal(result.Ok.ObjectAttributes, decoded.Ok.ObjectAttributes);
        Assert.Equal(result.Ok.DirectoryAttributes, decoded.Ok.DirectoryAttributes);
    }

    [Fact]
    public void LookupResult_Failure_RoundTripsWithFailData()
    {
        var result = Nfs3LookupResult.Failure(
            NfsStatus.NoEntry,
            new Nfs3LookupResultFail
            {
                DirectoryAttributes = new Nfs3FileAttributes { Type = NfsFileType.Directory, FileId = 2 },
            });

        Nfs3LookupResult decoded = RoundTrip(result);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(NfsStatus.NoEntry, decoded.Status);
        Assert.Equal(result.Fail.DirectoryAttributes, decoded.Fail.DirectoryAttributes);
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
}
