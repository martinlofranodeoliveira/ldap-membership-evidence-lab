using Novell.Directory.Ldap;

namespace MembershipEvidence;

internal sealed record RawEntry(string Dn, IReadOnlyDictionary<string, string[]> Attributes)
{
    internal string[] Values(string name) => Attributes.TryGetValue(name, out var values) ? values : [];
    internal string One(string name)
    {
        var values = Values(name);
        if (values.Length != 1 || values[0].Length == 0) throw new ObservationFailure("INVALID_ENTRY");
        return values[0];
    }
}
internal interface IDirectorySource : IDisposable
{
    Task<RawEntry?> Read(string dn, Budget budget);
    Task<IDisposable> Lock(Budget budget);
    Publication Publish(Resolution resolution, Budget budget);
}
internal sealed class LdapSource(int port, string directory) : IDirectorySource
{
    private readonly LdapConnection connection = new() { ConnectionTimeout = 500 };
    private bool connected;
    private static readonly string[] Attributes = ["objectClass", "entryUUID", "cn", "uid", "sn", "displayName", "member", "description"];
    public async Task<RawEntry?> Read(string dn, Budget budget)
    {
        budget.Check();
        try
        {
            connection.Constraints = new LdapConstraints
            {
                ReferralFollowing = false, TimeLimit = budget.RemainingMilliseconds
            };
            if (!connected)
            {
                connection.ConnectionTimeout = Math.Min(500, budget.RemainingMilliseconds);
                budget.Check();
                await connection.ConnectAsync("127.0.0.1", port, budget.Token);
                budget.Check();
                connected = true;
            }
            var constraints = new LdapSearchConstraints
            {
                ReferralFollowing = false,
                Dereference = LdapSearchConstraints.DerefNever,
                MaxResults = 2,
                ServerTimeLimit = 1,
                TimeLimit = budget.RemainingMilliseconds
            };
            budget.Check();
            var entry = await connection.ReadAsync(dn, Attributes, constraints, budget.Token);
            budget.Check();
            var values = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var attribute in Attributes)
            {
                var data = entry.GetOrDefault(attribute)?.StringValueArray ?? [];
                if (data.Length > (attribute == "member" ? 4097 : 16) || data.Any(s => s.Length > 2048))
                    throw new ObservationFailure("ENTRY_LIMIT");
                values.Add(attribute, data);
            }
            if (entry.Dn.Length > 2048) throw new ObservationFailure("ENTRY_LIMIT");
            return new RawEntry(entry.Dn, values);
        }
        catch (LdapException exception)
        {
            budget.Check();
            if (exception.ResultCode == LdapException.NoSuchObject) return null;
            throw new ObservationFailure("LDAP_ERROR");
        }
        catch (OperationCanceledException)
        {
            budget.Check();
            throw new ObservationFailure("LDAP_ERROR");
        }
    }
    public Task<IDisposable> Lock(Budget budget) => WriterLock.Acquire(directory, budget);
    public Publication Publish(Resolution resolution, Budget budget) => EvidenceStore.Publish(directory, resolution, budget);
    public void Dispose() => connection.Dispose();
}
internal static class GuardParser
{
    internal static Guard Parse(RawEntry? entry)
    {
        var descriptions = entry?.Values("description") ?? [];
        if (descriptions.Length != 2) throw new ObservationFailure("INVALID_GUARD");
        var epochs = descriptions.Where(s => s.StartsWith("epoch=", StringComparison.Ordinal)).ToArray();
        var revisions = descriptions.Where(s => s.StartsWith("revision=", StringComparison.Ordinal)).ToArray();
        if (epochs.Length != 1 || revisions.Length != 1) throw new ObservationFailure("INVALID_GUARD");
        var epoch = epochs[0][6..];
        var revision = revisions[0][9..];
        if (!Guid.TryParseExact(epoch, "D", out var guid) || guid == Guid.Empty || epoch != guid.ToString("D") ||
            revision.Length == 0 || revision.Length > 19 || revision.Any(c => c is < '0' or > '9') ||
            revision.Length > 1 && revision[0] == '0' ||
            !long.TryParse(revision, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new ObservationFailure("INVALID_GUARD");
        return new Guard(epoch, value);
    }
}
