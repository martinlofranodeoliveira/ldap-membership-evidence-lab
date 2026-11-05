namespace MembershipEvidence;

internal static class Resolver
{
    internal const string GuardDn = "cn=revision,dc=example,dc=test";
    internal static void ValidateArguments(string rootDn, Limits limits)
    {
        limits.Validate();
        if (string.IsNullOrWhiteSpace(rootDn) || rootDn.Length > 2048 || rootDn.Any(c => c is '\0' or '\r' or '\n'))
            throw new ArgumentException("A bounded base-object DN is required.", nameof(rootDn));
    }
    internal static async Task<Resolution> ResolveGroup(IDirectorySource source, string rootDn,
        Limits limits, bool publish, CancellationToken token, Budget? operationBudget = null)
    {
        ValidateArguments(rootDn, limits);
        var budget = operationBudget ?? new Budget(limits, token);
        var graph = new Graph();
        var errors = new SortedSet<string>(StringComparer.Ordinal);
        var cache = new Dictionary<string, Node?>(StringComparer.Ordinal);
        Guard? before = null;
        Guard? after = null;
        async Task<RawEntry?> Read(string dn)
        {
            budget.Request();
            var entry = await source.Read(dn, budget);
            budget.Check();
            return entry;
        }
        async Task<Node?> Fetch(string dn)
        {
            budget.Check();
            if (cache.TryGetValue(dn, out var cached)) return cached;
            var raw = await Read(dn);
            var node = raw is null ? null : graph.Add(Node.Parse(raw), limits);
            cache.Add(dn, node);
            return node;
        }
        try
        {
            before = GuardParser.Parse(await Read(GuardDn));
            if (before.Revision % 2 != 0) throw new ObservationFailure("ODD_REVISION");
            var root = await Fetch(rootDn);
            if (root is null || !root.Group) throw new ObservationFailure("INVALID_ROOT");
            graph.Root = root.Uuid;
            var visited = new HashSet<Guid>();
            var wave = new List<Node> { root };
            while (wave.Count != 0)
            {
                budget.Check();
                var next = new Dictionary<Guid, Node>();
                foreach (var group in wave.OrderBy(n => n.Id, StringComparer.Ordinal))
                {
                    if (!visited.Add(group.Uuid)) continue;
                    foreach (var member in group.Members.Order(StringComparer.Ordinal))
                    {
                        budget.Check();
                        if (graph.ObservedEdges == limits.Edges) throw new ObservationFailure("EDGE_CAP");
                        graph.ObservedEdges++;
                        var target = await Fetch(member);
                        budget.Check();
                        if (target is null)
                        {
                            graph.Missing.Add(new UnresolvedReference(group.Id, member));
                            errors.Add("MISSING_REFERENCE");
                            continue;
                        }
                        graph.Links[group.Uuid].Add(target.Uuid);
                        if (target.Group && !visited.Contains(target.Uuid)) next[target.Uuid] = target;
                    }
                }
                wave = next.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
            }
        }
        catch (ObservationFailure failure) { errors.Add(failure.Code); }
        IDisposable? writerLock = null;
        try
        {
            if (before is not null && before.Revision % 2 == 0)
            {
                budget.Check();
                if (publish)
                {
                    writerLock = await source.Lock(budget);
                    budget.Check();
                }
                after = GuardParser.Parse(await Read(GuardDn));
                if (after != before || after.Revision % 2 != 0) errors.Add("REVISION_CHANGED");
            }
            var result = graph.Build(errors, before, after, budget.Requests);
            if (publish && result.Complete)
            {
                budget.Check();
                try { result = result with { Publication = source.Publish(result, budget) }; }
                catch (ObservationFailure failure)
                {
                    errors.Add(failure.Code);
                    result = graph.Build(errors, before, after, budget.Requests);
                }
            }
            return result;
        }
        catch (ObservationFailure failure)
        {
            errors.Add(failure.Code);
            return graph.Build(errors, before, after, budget.Requests);
        }
        finally { writerLock?.Dispose(); }
    }
}
