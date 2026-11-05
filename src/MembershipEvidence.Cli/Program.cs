using System.Text.Json;
using MembershipEvidence;

if (args.Length == 0 || args[0] != "demo" || args.Skip(1).Any(a => a is not ("--assert" or "--json")))
{
    Console.Error.WriteLine("Usage: MembershipEvidence.Cli demo [--assert] [--json]");
    return 2;
}
try
{
    await using var fixture = await SyntheticFixture.Create();
    var original = await fixture.ResolveAndExport(SyntheticFixture.RootDn);
    var oracle = await fixture.Oracle();
    var b = original.Principals.Single(p => p.Id == "B");
    await fixture.Apply(FixtureMutation.AddCycle);
    var cyclic = await fixture.ResolveGroup(SyntheticFixture.RootDn);
    await fixture.Apply(FixtureMutation.AddMissingReference);
    var missing = await fixture.ResolveAndExport(SyntheticFixture.RootDn);
    await using var replacementFixture = await SyntheticFixture.Create();
    var replacementBefore = await replacementFixture.ResolveGroup(SyntheticFixture.RootDn);
    await replacementFixture.Apply(FixtureMutation.ReplaceG2WithD);
    var replacement = await replacementFixture.ResolveAndExport(SyntheticFixture.RootDn);
    var difference = MembershipDifference.Compare(replacementBefore, replacement);
    var cap = await replacementFixture.ResolveAndExport(SyntheticFixture.RootDn, new Limits(Groups: 2));
    var exports = new[] { original, missing, replacement, cap }.Count(r => r.Publication?.Created == true);
    var valid = original.Complete && original.Groups.Count == 3 && original.Observation.ObservedEdges == 6 &&
        original.Principals.Select(p => p.Id).SequenceEqual(new[] { "A", "B", "C" }) &&
        b.Path.SequenceEqual(new[] { "root", "G1", "B" }) && b.ShortestPathCount == "2" &&
        cyclic.Principals.Count == 3 && cyclic.CyclicComponents.Count == 1 &&
        cyclic.CyclicComponents[0].SequenceEqual(new[] { "G2", "root" }) &&
        missing.Principals.Count == 3 && missing.Unresolved.Count == 1 && !missing.Complete && missing.Publication is null &&
        replacement.Principals.Select(p => p.Id).SequenceEqual(new[] { "A", "B", "D" }) &&
        difference.Added.SequenceEqual(new[] { "D" }) && difference.Removed.SequenceEqual(new[] { "C" }) &&
        cap.Errors.Contains("GROUP_CAP") && !cap.Complete && cap.Publication is null &&
        oracle == new OracleReport(3, 6, 4) && exports == 2;
    if (args.Contains("--assert") && !valid) throw new FixtureException("DEMO_ASSERTION_FAILED");
    var report = new
    {
        contract = "0.1.0",
        principals = original.Principals.Select(p => p.Id), groups = original.Groups.Count,
        edges = original.Observation.ObservedEdges, bPath = b.Path, bShortestPathCount = b.ShortestPathCount,
        cyclicComponents = cyclic.CyclicComponents,
        missing = new { known = missing.Principals.Count, unresolved = missing.Unresolved.Count, complete = missing.Complete, published = missing.Publication is not null },
        replacement = new { principals = replacement.Principals.Select(p => p.Id), added = difference.Added.Count, removed = difference.Removed.Count },
        cap = cap.Errors.FirstOrDefault(), oracle, stableExports = exports
    };
    if (!args.Contains("--json"))
    {
        Console.WriteLine("LDAP Membership Evidence Lab — development 0.1.0");
        Console.WriteLine("Original: A, B, C; 3 groups; 6 edges. B: root → G1 → B (2 shortest paths).");
        Console.WriteLine("Cycle: {G2, root}. Missing member: incomplete, no export.");
        Console.WriteLine("Replacement: A, B, D; +1 / -1. Group cap: incomplete, no export.");
        Console.WriteLine("ldapsearch: 3 groups, 6 total links, 4 below G1/G2. Stable exports: 2.");
        Console.WriteLine("JSON:");
    }
    Console.WriteLine(JsonSerializer.Serialize(report, EvidenceJson.Options));
    return 0;
}
catch (FixtureException exception) { Console.Error.WriteLine(exception.Code); return 1; }
catch (OperationCanceledException) { Console.Error.WriteLine("CANCELLED"); return 1; }
catch (Exception) { Console.Error.WriteLine("DEMO_FAILED"); return 1; }
