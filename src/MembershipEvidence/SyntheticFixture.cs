using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace MembershipEvidence;

public sealed class SyntheticFixture : IAsyncDisposable
{
    public const string RootDn = "cn=root,ou=groups,dc=example,dc=test";
    private readonly string directory;
    private readonly int port;
    private NativeOwner? server;
    private bool disposed;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly HashSet<string> published = new(StringComparer.Ordinal);
    private SyntheticFixture(string directory, int port) { this.directory = directory; this.port = port; }
    [DllImport("libc")]
    private static extern uint getuid();
    [DllImport("libc")]
    private static extern uint getgid();
    public static async Task<SyntheticFixture> Create(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux() || getuid() == 0) throw new FixtureException("UNPRIVILEGED_LINUX_REQUIRED");
        var directory = Path.Combine(Path.GetTempPath(), "ldap-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var fixture = new SyntheticFixture(directory, port);
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "db"));
            Directory.CreateDirectory(Path.Combine(directory, "evidence"));
            File.WriteAllText(Path.Combine(directory, "ldap.conf"), "# Private synthetic fixture; LDAPNOINIT is also set.\n");
            var epoch = Guid.NewGuid().ToString("D");
            var configuration = $"include /etc/ldap/schema/core.schema\ninclude /etc/ldap/schema/cosine.schema\ninclude /etc/ldap/schema/inetorgperson.schema\npidfile {directory}/slapd.pid\nargsfile {directory}/slapd.args\nmodulepath /usr/lib/ldap\nmoduleload back_mdb\nthreads 2\nloglevel 0\nsizelimit 1100\ntimelimit 1\ndatabase mdb\nmaxsize 16777216\nsuffix dc=example,dc=test\ndirectory {directory}/db\nindex entryUUID eq\naccess to * by dn.exact=\"gidNumber={getgid()}+uidNumber={getuid()},cn=peercred,cn=external,cn=auth\" manage by * read\n";
            File.WriteAllText(Path.Combine(directory, "slapd.conf"), configuration);
            File.WriteAllText(Path.Combine(directory, "seed.ldif"), Seed.Ldif(epoch), new UTF8Encoding(false));
            await NativeOwner.Run("seed", directory, port, token);
            fixture.server = NativeOwner.Start("server", directory, port);
            var started = Stopwatch.GetTimestamp();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (fixture.server.HasExited || Stopwatch.GetElapsedTime(started).TotalSeconds >= 5)
                    throw new FixtureException("FIXTURE_START_FAILED");
                try
                {
                    using var source = new LdapSource(port, directory);
                    var budget = new Budget(new Limits(Milliseconds: 500), token);
                    budget.Request();
                    var guard = GuardParser.Parse(await source.Read(Resolver.GuardDn, budget));
                    if (guard.Epoch != epoch || guard.Revision != 2) throw new FixtureException("FIXTURE_IDENTITY_FAILED");
                    return fixture;
                }
                catch (ObservationFailure) { await Task.Delay(25, token); }
            }
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }
    public Task<Resolution> ResolveGroup(string rootDn, Limits? limits = null, CancellationToken token = default) =>
        Observe(rootDn, limits ?? new Limits(), false, token);
    public Task<Resolution> ResolveAndExport(string rootDn, Limits? limits = null, CancellationToken token = default) =>
        Observe(rootDn, limits ?? new Limits(), true, token);
    private async Task<Resolution> Observe(string rootDn, Limits limits, bool export, CancellationToken token)
    {
        Resolver.ValidateArguments(rootDn, limits);
        var budget = new Budget(limits, token);
        var entered = false;
        Resolution NotStarted(string code) => new Graph().Build(
            new SortedSet<string>(StringComparer.Ordinal) { code }, null, null, budget.Requests);
        try
        {
            entered = await operations.WaitAsync(budget.RemainingMilliseconds, token);
            if (!entered) return NotStarted("DEADLINE");
            budget.Check();
            ObjectDisposedException.ThrowIf(disposed, this);
            using var source = new LdapSource(port, directory);
            var result = await Resolver.ResolveGroup(source, rootDn, limits, export, token, budget);
            if (result.Publication is not null) published.Add(result.Publication.FileName);
            return result;
        }
        catch (ObservationFailure failure) { return NotStarted(failure.Code); }
        catch (OperationCanceledException) { return NotStarted("CANCELLED"); }
        finally { if (entered) operations.Release(); }
    }
    public async Task<byte[]> ReadEvidence(string fileName, CancellationToken token = default)
    {
        await operations.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!published.Contains(fileName)) throw new ArgumentException("Unknown generation.", nameof(fileName));
            return await File.ReadAllBytesAsync(Path.Combine(directory, "evidence", fileName), token);
        }
        finally { operations.Release(); }
    }
    public async Task Apply(FixtureMutation mutation, CancellationToken token = default)
    {
        if (!Enum.IsDefined(mutation)) throw new ArgumentOutOfRangeException(nameof(mutation));
        await operations.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var budget = new Budget(new Limits(), token);
            using var writer = await WriterLock.Acquire(directory, budget);
            using var source = new LdapSource(port, directory);
            budget.Request();
            var guard = GuardParser.Parse(await source.Read(Resolver.GuardDn, budget));
            if (guard.Revision % 2 != 0) throw new FixtureException("ODD_REVISION_REBUILD_REQUIRED");
            if (guard.Revision > long.MaxValue - 2) throw new FixtureException("REVISION_EXHAUSTED");
            await Modify(Seed.Revision(guard with { Revision = guard.Revision + 1 }), token);
            await Modify(Seed.Mutation(mutation), token);
            await Modify(Seed.Revision(guard with { Revision = guard.Revision + 2 }), token);
        }
        catch (ObservationFailure failure) { throw new FixtureException(failure.Code); }
        finally { operations.Release(); }
    }
    private async Task Modify(string ldif, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await File.WriteAllTextAsync(Path.Combine(directory, "batch.ldif"), ldif, new UTF8Encoding(false), token);
        await NativeOwner.Run("modify", directory, port, token);
    }
    public async Task<OracleReport> Oracle(CancellationToken token = default)
    {
        await operations.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var text = await NativeOwner.Run("oracle", directory, port, token);
            var groups = 0;
            var total = 0;
            var below = 0;
            foreach (var block in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (!lines.Any(l => l.StartsWith("dn: ", StringComparison.Ordinal))) continue;
                var ids = lines.Where(l => l.StartsWith("cn: ", StringComparison.Ordinal)).ToArray();
                if (ids.Length != 1 || lines.Any(l => l.StartsWith("member::", StringComparison.Ordinal)))
                    throw new FixtureException("ORACLE_FORMAT");
                var links = lines.Count(l => l.StartsWith("member: ", StringComparison.Ordinal));
                groups++;
                total += links;
                if (ids[0] != "cn: root") below += links;
            }
            return new OracleReport(groups, total, below);
        }
        finally { operations.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        await operations.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            if (server is not null) await server.DisposeAsync();
            DeleteOwnedTree(directory);
        }
        finally { operations.Release(); }
    }
    private static void DeleteOwnedTree(string path)
    {
        foreach (var item in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(item);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                File.Delete(item);
            else DeleteOwnedTree(item);
        }
        Directory.Delete(path);
    }
}
