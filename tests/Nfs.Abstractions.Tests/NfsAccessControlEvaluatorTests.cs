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

    [Fact]
    public void Evaluate_AllowBeforeDeny_FirstMatchWins()
    {
        NfsAccessControlEntry[] acl =
        [
            new(NfsAceType.Allow, NfsAceDescriptor.None, NfsAceAccessMask.WriteData, "alice"),
            new(NfsAceType.Deny, NfsAceDescriptor.None, NfsAceAccessMask.WriteData, "alice"),
        ];

        (NfsAceAccessMask granted, NfsAceAccessMask denied) =
            NfsAccessControlEvaluator.Evaluate(acl, "alice", NfsAceAccessMask.WriteData);

        Assert.Equal(NfsAceAccessMask.WriteData, granted);
        Assert.Equal(NfsAceAccessMask.None, denied);
    }

    [Fact]
    public void Evaluate_EveryoneAce_AppliesToNamedPrincipal()
    {
        NfsAccessControlEntry[] acl =
        [
            new(NfsAceType.Allow, NfsAceDescriptor.None, NfsAceAccessMask.ReadData, "EVERYONE@"),
        ];

        (NfsAceAccessMask granted, NfsAceAccessMask denied) =
            NfsAccessControlEvaluator.Evaluate(acl, "alice", NfsAceAccessMask.ReadData);

        Assert.Equal(NfsAceAccessMask.ReadData, granted);
        Assert.Equal(NfsAceAccessMask.None, denied);
    }

    [Fact]
    public void Evaluate_EmptyAcl_DeniesEverythingRequested()
    {
        (NfsAceAccessMask granted, NfsAceAccessMask denied) = NfsAccessControlEvaluator.Evaluate(
            [],
            "alice",
            NfsAceAccessMask.ReadData | NfsAceAccessMask.WriteData);

        Assert.Equal(NfsAceAccessMask.None, granted);
        Assert.Equal(NfsAceAccessMask.ReadData | NfsAceAccessMask.WriteData, denied);
    }

    [Fact]
    public void Evaluate_PrincipalMatchesNoAce_DeniesEverythingRequested()
    {
        NfsAccessControlEntry[] acl =
        [
            new(NfsAceType.Allow, NfsAceDescriptor.None, NfsAceAccessMask.ReadData, "bob"),
        ];

        (NfsAceAccessMask granted, NfsAceAccessMask denied) =
            NfsAccessControlEvaluator.Evaluate(acl, "alice", NfsAceAccessMask.ReadData);

        Assert.Equal(NfsAceAccessMask.None, granted);
        Assert.Equal(NfsAceAccessMask.ReadData, denied);
    }

    [Fact]
    public void Evaluate_MatchingAceWithoutOverlappingBits_LeavesRequestedBitDenied()
    {
        NfsAccessControlEntry[] acl =
        [
            new(NfsAceType.Allow, NfsAceDescriptor.None, NfsAceAccessMask.ReadData, "alice"),
        ];

        (NfsAceAccessMask granted, NfsAceAccessMask denied) =
            NfsAccessControlEvaluator.Evaluate(acl, "alice", NfsAceAccessMask.Execute);

        Assert.Equal(NfsAceAccessMask.None, granted);
        Assert.Equal(NfsAceAccessMask.Execute, denied);
    }

    [Fact]
    public void Evaluate_RequestingNoAccess_GrantsAndDeniesNothing()
    {
        NfsAccessControlEntry[] acl =
        [
            new(NfsAceType.Allow, NfsAceDescriptor.None, NfsAceAccessMask.ReadData, "alice"),
        ];

        (NfsAceAccessMask granted, NfsAceAccessMask denied) =
            NfsAccessControlEvaluator.Evaluate(acl, "alice", NfsAceAccessMask.None);

        Assert.Equal(NfsAceAccessMask.None, granted);
        Assert.Equal(NfsAceAccessMask.None, denied);
    }

    [Fact]
    public void Evaluate_NullEntries_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            NfsAccessControlEvaluator.Evaluate(null!, "alice", NfsAceAccessMask.ReadData));

    [Fact]
    public void Evaluate_NullPrincipal_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            NfsAccessControlEvaluator.Evaluate([], null!, NfsAceAccessMask.ReadData));
}
