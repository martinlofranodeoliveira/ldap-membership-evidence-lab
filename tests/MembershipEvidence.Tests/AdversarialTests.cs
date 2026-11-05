using Xunit;

namespace MembershipEvidence.Tests;

public sealed class AdversarialTests
{
    private static Task<Resolution> Resolve(ScriptedSource source, Limits? limits = null, CancellationToken token = default) =>
        Resolver.ResolveGroup(source, Seed.RootDn, limits ?? new Limits(), true, token);
    [Fact]
    public async Task RevisionTwoToThreeRejectsAndLaterFourPublishesOnce()
    {
        using var changed = new ScriptedSource { Start = 2, End = 3 };
        var rejected = await Resolve(changed, token: TestContext.Current.CancellationToken);
        Assert.Contains("REVISION_CHANGED", rejected.Errors);
        Assert.False(rejected.Complete);
        Assert.Equal(0, changed.Publications);
        Assert.Equal(3, rejected.Principals.Count);
        using var stable = new ScriptedSource { Start = 4, End = 4 };
        Assert.True((await Resolve(stable, token: TestContext.Current.CancellationToken)).Complete);
        Assert.Equal(1, stable.Publications);
    }
    [Fact]
    public async Task EpochChangePreventsAbaAndOddRevisionBlocksTraversal()
    {
        using var rebuilt = new ScriptedSource { EndEpoch = "a90252f6-ae53-4f87-b730-b0c942411590" };
        Assert.Contains("REVISION_CHANGED", (await Resolve(rebuilt, token: TestContext.Current.CancellationToken)).Errors);
        Assert.Equal(0, rebuilt.Publications);
        using var odd = new ScriptedSource { Start = 3, End = 3 };
        var result = await Resolve(odd, token: TestContext.Current.CancellationToken);
        Assert.Contains("ODD_REVISION", result.Errors);
        Assert.Empty(result.Principals);
        Assert.Equal(1, odd.Reads);
        Assert.Equal(0, odd.Publications);
    }
    [Fact]
    public async Task AlreadyCancelledTokenNeverDispatchesEvenIfSourceIgnoresCancellation()
    {
        using var source = new ScriptedSource();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await Resolve(source, token: cancellation.Token);
        Assert.Contains("CANCELLED", result.Errors);
        Assert.Equal(0, source.Reads);
        Assert.Equal(0, result.Observation.Requests);
        Assert.Equal(0, source.Publications);
    }
    [Fact]
    public async Task PostAwaitDeadlineCheckRejectsAnUncooperativeFastResult()
    {
        using var source = new ScriptedSource { Delay = 40 };
        var result = await Resolve(source, new Limits(Milliseconds: 5), token: TestContext.Current.CancellationToken);
        Assert.Contains("DEADLINE", result.Errors);
        Assert.Equal(1, source.Reads);
        Assert.Empty(result.Principals);
        Assert.Equal(0, source.Publications);
    }
    [Fact]
    public async Task MalformedDuplicateGuardDoesNotBecomeRevisionZero()
    {
        using var source = new ScriptedSource { Malformed = true };
        var result = await Resolve(source, token: TestContext.Current.CancellationToken);
        Assert.Contains("INVALID_GUARD", result.Errors);
        Assert.Equal(0, source.Publications);
    }
    [Fact]
    public async Task UuidIdentityDeduplicatesDistinctReturnedDnSpellings()
    {
        using var source = new ScriptedSource();
        const string alternate = "UID=B,OU=people,DC=example,DC=test";
        source.Entries[alternate] = source.Entries[Seed.Person("B")] with { Dn = alternate };
        source.Entries[Seed.G2Dn] = ScriptedSource.Entry(Seed.G2Dn,
            "00000000-0000-0000-0000-000000000003", "G2", true, [alternate, Seed.Person("C")]);
        var result = await Resolve(source, token: TestContext.Current.CancellationToken);
        Assert.True(result.Complete);
        Assert.Equal(3, result.Principals.Count);
        Assert.Equal("2", result.Principals.Single(p => p.Id == "B").ShortestPathCount);
    }
    [Fact]
    public async Task SelfLoopIsOneComponentAndInputOrderDoesNotChooseBPath()
    {
        using var source = new ScriptedSource();
        source.Entries[Seed.RootDn] = ScriptedSource.Entry(Seed.RootDn,
            "00000000-0000-0000-0000-000000000001", "root", true, [Seed.G2Dn, Seed.RootDn, Seed.G1Dn]);
        var result = await Resolve(source, token: TestContext.Current.CancellationToken);
        Assert.True(result.Complete);
        Assert.Equal(new[] { "root" }, Assert.Single(result.CyclicComponents).ToArray());
        Assert.Equal(new[] { "root", "G1", "B" }, result.Principals.Single(p => p.Id == "B").Path.ToArray());
        Assert.Equal("2", result.Principals.Single(p => p.Id == "B").ShortestPathCount);
    }
    [Fact]
    public async Task UnknownSchemaIsNotSilentlyTreatedAsAPrincipal()
    {
        using var source = new ScriptedSource();
        source.Entries[Seed.Person("C")] = new RawEntry(Seed.Person("C"), new Dictionary<string, string[]>
        {
            ["entryUUID"] = ["00000000-0000-0000-0000-000000000006"], ["objectClass"] = ["applicationProcess"]
        });
        var result = await Resolve(source, token: TestContext.Current.CancellationToken);
        Assert.Contains("UNSUPPORTED_ENTRY", result.Errors);
        Assert.False(result.Complete);
        Assert.NotEmpty(result.Principals);
        Assert.Equal(0, source.Publications);
    }
}
