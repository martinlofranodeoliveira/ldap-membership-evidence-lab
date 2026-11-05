using System.Text;
using System.Text.Json;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace MembershipEvidence.Tests;

public sealed class IntegrationTests
{
    [Fact]
    public async Task OriginalDirectoryMatchesIndependentOracleAndPreservesUnicode()
    {
        await using var fixture = await SyntheticFixture.Create(token: TestContext.Current.CancellationToken);
        var result = await fixture.ResolveAndExport(SyntheticFixture.RootDn, token: TestContext.Current.CancellationToken);
        Assert.True(result.Complete);
        Assert.Equal(new[] { "A", "B", "C" }, result.Principals.Select(p => p.Id).ToArray());
        Assert.Equal(3, result.Groups.Count);
        Assert.Equal(6, result.Edges.Count);
        Assert.Equal(6, result.Observation.ObservedEdges);
        Assert.Equal(6, result.Groups.Select(g => g.EntryUuid).Concat(result.Principals.Select(p => p.EntryUuid)).Distinct().Count());
        var b = Assert.Single(result.Principals, p => p.Id == "B");
        Assert.Equal(new[] { "root", "G1", "B" }, b.Path.ToArray());
        Assert.Equal(2, b.Path.Count - 1);
        Assert.Equal("2", b.ShortestPathCount);
        Assert.Equal("Principal B sintético", b.DisplayName);
        Assert.Equal(new OracleReport(3, 6, 4), await fixture.Oracle(token: TestContext.Current.CancellationToken));
        Assert.NotNull(result.Publication);
        Assert.True(result.Publication.Created);
        var bytes = await fixture.ReadEvidence(result.Publication.FileName, token: TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(bytes);
        Assert.True(json.RootElement.GetProperty("complete").GetBoolean());
        Assert.Contains("sintético", Encoding.UTF8.GetString(bytes));
        var repeat = await fixture.ResolveAndExport(SyntheticFixture.RootDn, token: TestContext.Current.CancellationToken);
        Assert.NotNull(repeat.Publication);
        Assert.False(repeat.Publication.Created);
        Assert.Equal(bytes, await fixture.ReadEvidence(repeat.Publication.FileName, token: TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task ServerMatchesEquivalentDnSpellingsByUuid()
    {
        await using var fixture = await SyntheticFixture.Create(token: TestContext.Current.CancellationToken);
        var ordinary = await fixture.ResolveGroup("cn=G1,ou=groups,dc=example,dc=test", token: TestContext.Current.CancellationToken);
        var escaped = await fixture.ResolveGroup("CN=G\\31, OU=groups,DC=example,DC=test", token: TestContext.Current.CancellationToken);
        Assert.True(ordinary.Complete);
        Assert.True(escaped.Complete);
        Assert.Equal(Assert.Single(ordinary.Groups).EntryUuid, Assert.Single(escaped.Groups).EntryUuid);
        Assert.Equal(ordinary.Principals.Select(p => p.EntryUuid).ToArray(), escaped.Principals.Select(p => p.EntryUuid).ToArray());
    }
    [Fact]
    public async Task CyclicComponentAndMissingReferenceDoNotLoseKnownPrincipals()
    {
        await using var fixture = await SyntheticFixture.Create(token: TestContext.Current.CancellationToken);
        await fixture.Apply(FixtureMutation.AddCycle, token: TestContext.Current.CancellationToken);
        var cycle = await fixture.ResolveGroup(SyntheticFixture.RootDn, token: TestContext.Current.CancellationToken);
        Assert.True(cycle.Complete);
        Assert.Equal(3, cycle.Principals.Count);
        Assert.Equal(new[] { "G2", "root" }, Assert.Single(cycle.CyclicComponents).ToArray());
        Assert.Equal("2", cycle.Principals.Single(p => p.Id == "B").ShortestPathCount);
        await fixture.Apply(FixtureMutation.AddMissingReference, token: TestContext.Current.CancellationToken);
        var missing = await fixture.ResolveAndExport(SyntheticFixture.RootDn, token: TestContext.Current.CancellationToken);
        Assert.False(missing.Complete);
        Assert.Null(missing.Publication);
        Assert.Equal(new[] { "A", "B", "C" }, missing.Principals.Select(p => p.Id).ToArray());
        Assert.Equal("G1", Assert.Single(missing.Unresolved).Group);
        Assert.Contains("MISSING_REFERENCE", missing.Errors);
    }
    [Fact]
    public async Task AtomicMemberReplacementHasOneAdditionAndOneRemoval()
    {
        await using var fixture = await SyntheticFixture.Create(token: TestContext.Current.CancellationToken);
        var before = await fixture.ResolveGroup(SyntheticFixture.RootDn, token: TestContext.Current.CancellationToken);
        await fixture.Apply(FixtureMutation.ReplaceG2WithD, token: TestContext.Current.CancellationToken);
        var after = await fixture.ResolveAndExport(SyntheticFixture.RootDn, token: TestContext.Current.CancellationToken);
        Assert.True(after.Complete);
        Assert.Equal(2, before.Observation.Before!.Revision);
        Assert.Equal(4, after.Observation.Before!.Revision);
        Assert.Equal(new[] { "A", "B", "D" }, after.Principals.Select(p => p.Id).ToArray());
        var difference = MembershipDifference.Compare(before, after);
        Assert.Equal(new[] { "D" }, difference.Added.ToArray());
        Assert.Equal(new[] { "C" }, difference.Removed.ToArray());
    }
    [Fact]
    public async Task ResourceCapsNeverPublish()
    {
        await using var fixture = await SyntheticFixture.Create(token: TestContext.Current.CancellationToken);
        foreach (var pair in new[]
        {
            (new Limits(Groups: 2), "GROUP_CAP"),
            (new Limits(Principals: 1), "PRINCIPAL_CAP"),
            (new Limits(Edges: 3), "EDGE_CAP"),
            (new Limits(Requests: 2), "REQUEST_CAP")
        })
        {
            var result = await fixture.ResolveAndExport(SyntheticFixture.RootDn, pair.Item1, token: TestContext.Current.CancellationToken);
            Assert.False(result.Complete);
            Assert.Null(result.Publication);
            Assert.Contains(pair.Item2, result.Errors);
            Assert.InRange(result.Observation.Requests, 0, pair.Item1.Requests);
        }
        var partial = await fixture.ResolveGroup(SyntheticFixture.RootDn, new Limits(Edges: 3), token: TestContext.Current.CancellationToken);
        Assert.NotEmpty(partial.Principals);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedObservationHonorsCancellationAndDeadline(bool cancel)
    {
        await using var fixture = await SyntheticFixture.Create(token: TestContext.Current.CancellationToken);
        var field = typeof(SyntheticFixture).GetField("operations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var gate = (SemaphoreSlim)field.GetValue(fixture)!;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await gate.WaitAsync(TestContext.Current.CancellationToken);
        Task<Resolution>? queued = null;
        try
        {
            if (cancel) cancellation.CancelAfter(30);
            queued = fixture.ResolveAndExport(SyntheticFixture.RootDn,
                new Limits(Milliseconds: cancel ? 2000 : 30), token: cancellation.Token);
            var ready = await Task.WhenAny(queued, Task.Delay(500, TestContext.Current.CancellationToken));
            Assert.Same(queued, ready);
            var result = await queued;
            Assert.False(result.Complete);
            Assert.Contains(cancel ? "CANCELLED" : "DEADLINE", result.Errors);
            Assert.Equal(0, result.Observation.Requests);
            Assert.Null(result.Publication);
        }
        finally
        {
            gate.Release();
            if (queued is not null) _ = await queued;
        }
    }
}
