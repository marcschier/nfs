using Nfs.Xdr;

namespace Nfs.Xdr.Generator.Tests;

public enum Color : uint
{
    Red = 1,
    Green = 2,
    Blue = 0xFFFFFFFF,
}

[XdrType]
public partial struct Primitives
{
    [XdrField(0)]
    public int A;

    [XdrField(1)]
    public uint B;

    [XdrField(2)]
    public long C;

    [XdrField(3)]
    public ulong D;

    [XdrField(4)]
    public bool E;
}

[XdrType]
public partial struct EnumStringOpaque
{
    [XdrField(0)]
    public Color Color;

    [XdrField(1)]
    [XdrString(64)]
    public string Name;

    [XdrField(2)]
    [XdrOpaque(1024)]
    public byte[] Data;
}

[XdrType]
public partial struct FileHandle
{
    [XdrField(0)]
    [XdrFixedOpaque(8)]
    public byte[] Bytes;
}

[XdrType]
public partial struct Nested
{
    [XdrField(0)]
    public int Tag;

    [XdrField(1)]
    public Primitives Inner;
}

[XdrType]
public partial struct WithOptionals
{
    [XdrField(0)]
    public int? MaybeInt;

    [XdrField(1)]
    public Primitives? MaybeStruct;

    [XdrField(2)]
    public Color? MaybeColor;
}

[XdrType]
public partial struct WithArrays
{
    [XdrField(0)]
    [XdrArray(100)]
    public uint[] Numbers;

    [XdrField(1)]
    [XdrArray(10)]
    public Primitives[] Items;
}
