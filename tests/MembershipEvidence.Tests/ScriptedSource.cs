namespace MembershipEvidence.Tests;

internal sealed class ScriptedSource : IDirectorySource
{
    internal string Epoch = "8b446877-954d-4a47-a8b2-ef3d9e0c7b89";
    internal long Start = 2;
    internal long End = 2;
    internal string? EndEpoch;
    internal bool Malformed;
    internal int Reads;
    internal int Publications;
    internal int Delay;
    private int guards;
    internal readonly Dictionary<string, RawEntry> Entries = new(StringComparer.Ordinal)
    {
        [Seed.RootDn] = Entry(Seed.RootDn, "00000000-0000-0000-0000-000000000001", "root", true, [Seed.G1Dn, Seed.G2Dn]),
        [Seed.G1Dn] = Entry(Seed.G1Dn, "00000000-0000-0000-0000-000000000002", "G1", true, [Seed.Person("A"), Seed.Person("B")]),
        [Seed.G2Dn] = Entry(Seed.G2Dn, "00000000-0000-0000-0000-000000000003", "G2", true, [Seed.Person("B"), Seed.Person("C")]),
        [Seed.Person("A")] = Entry(Seed.Person("A"), "00000000-0000-0000-0000-000000000004", "A", false, []),
        [Seed.Person("B")] = Entry(Seed.Person("B"), "00000000-0000-0000-0000-000000000005", "B", false, []),
        [Seed.Person("C")] = Entry(Seed.Person("C"), "00000000-0000-0000-0000-000000000006", "C", false, [])
    };
    internal static RawEntry Entry(string dn, string uuid, string id, bool group, string[] members) =>
        new(dn, new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["entryUUID"] = [uuid], ["cn"] = [id], ["uid"] = [id], ["sn"] = ["Synthetic"],
            ["displayName"] = [$"Principal {id} sintético"], ["member"] = members,
            ["objectClass"] = group ? ["top", "groupOfNames"] : ["top", "person", "organizationalPerson", "inetOrgPerson"]
        });
    public async Task<RawEntry?> Read(string dn, Budget budget)
    {
        Reads++;
        if (Delay != 0) await Task.Delay(Delay);
        if (dn != Resolver.GuardDn) return Entries.GetValueOrDefault(dn);
        var first = guards++ == 0;
        return new RawEntry(dn, new Dictionary<string, string[]>
        {
            ["description"] = Malformed ? ["epoch=" + Epoch, "revision=2", "revision=4"] :
                ["epoch=" + (first ? Epoch : EndEpoch ?? Epoch), "revision=" + (first ? Start : End).ToString(System.Globalization.CultureInfo.InvariantCulture)]
        });
    }
    public Task<IDisposable> Lock(Budget budget) => Task.FromResult<IDisposable>(new EmptyLock());
    public Publication Publish(Resolution resolution, Budget budget)
    {
        Publications++;
        return new Publication("synthetic-evidence.json", true);
    }
    public void Dispose() { }
    private sealed class EmptyLock : IDisposable { public void Dispose() { } }
}
