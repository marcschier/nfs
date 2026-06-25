using Xunit;

namespace Nfs.Abstractions.Tests;

public sealed class NfsAccessControlEvaluatorTests
{
    [Fact]
    public void Evaluate_StopsDeniedAndGrantedBitsInAclOrder()
    {
        NfsAccessControlEntry[] acl =
        [
            new(NfsAceType.Deny, NfsAceDescriptor.None, NfsAceAccessMask.WriteData, "alice"),
            new(NfsAceType.Allow, NfsAceDescriptor.None, NfsAceAccessMask.ReadData | NfsAceAccessMask.WriteData, "alice"),
            new(NfsAceType.Allow, NfsAceDescriptor.None, NfsAceAccessMask.Execute, "EVERYONE@"),
        ];

        (NfsAceAccessMask granted, NfsAceAccessMask denied) = NfsAccessControlEvaluator.Evaluate(
            acl,
            "alice",
            NfsAceAccessMask.ReadData | NfsAceAccessMask.WriteData | NfsAceAccessMask.Execute | NfsAceAccessMask.Delete);

        Assert.Equal(NfsAceAccessMask.ReadData | NfsAceAccessMask.Execute, granted);
        Assert.Equal(NfsAceAccessMask.WriteData | NfsAceAccessMask.Delete, denied);
    }
}
