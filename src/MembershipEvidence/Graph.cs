using System.Globalization;
using System.Numerics;

namespace MembershipEvidence;

internal sealed record Node(Guid Uuid, string Id, string Dn, string Display, bool Group, string[] Members)
{
    internal static Node Parse(RawEntry entry)
    {
        if (!Guid.TryParseExact(entry.One("entryUUID"), "D", out var uuid) || uuid == Guid.Empty)
            throw new ObservationFailure("INVALID_ENTRY");
        var classes = entry.Values("objectClass");
        var group = classes.Contains("groupOfNames", StringComparer.OrdinalIgnoreCase);
        var principal = classes.Contains("inetOrgPerson", StringComparer.OrdinalIgnoreCase);
        if (group == principal) throw new ObservationFailure("UNSUPPORTED_ENTRY");
        var allowed = group ? new[] { "top", "groupOfNames" } : new[] { "top", "person", "organizationalPerson", "inetOrgPerson" };
        if (classes.Any(c => !allowed.Contains(c, StringComparer.OrdinalIgnoreCase)))
            throw new ObservationFailure("UNSUPPORTED_ENTRY");
        var id = entry.One(group ? "cn" : "uid");
        if (id.Length > 64 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-'))
            throw new ObservationFailure("INVALID_ENTRY");
        if (!group && (entry.One("sn") != "Synthetic" || entry.One("cn") != id))
            throw new ObservationFailure("UNSUPPORTED_ENTRY");
        var members = entry.Values("member");
        if (group && members.Length == 0 || members.Any(s => s.Length == 0 || s.Contains('\0')))
            throw new ObservationFailure("INVALID_ENTRY");
        return new Node(uuid, id, entry.Dn, group ? entry.One("cn") : entry.One("displayName"), group, members);
    }
}
internal sealed class Graph
{
    internal readonly Dictionary<Guid, Node> Nodes = [];
    internal readonly Dictionary<Guid, HashSet<Guid>> Links = [];
    internal readonly List<UnresolvedReference> Missing = [];
    internal Guid? Root;
    internal int ObservedEdges;
    internal int GroupCount => Nodes.Values.Count(n => n.Group);
    internal int PrincipalCount => Nodes.Count - GroupCount;

    internal Node Add(Node node, Limits limits)
    {
        if (Nodes.TryGetValue(node.Uuid, out var existing)) return existing;
        if (Nodes.Values.Any(n => n.Id == node.Id)) throw new ObservationFailure("DUPLICATE_IDENTIFIER");
        if (node.Group && GroupCount == limits.Groups) throw new ObservationFailure("GROUP_CAP");
        if (!node.Group && PrincipalCount == limits.Principals) throw new ObservationFailure("PRINCIPAL_CAP");
        Nodes.Add(node.Uuid, node);
        Links.Add(node.Uuid, []);
        return node;
    }
    internal Resolution Build(SortedSet<string> errors, Guard? before, Guard? after, int requests)
    {
        var distances = new Dictionary<Guid, int>();
        var paths = new Dictionary<Guid, string[]>();
        var counts = new Dictionary<Guid, BigInteger>();
        var queue = new Queue<Guid>();
        if (Root is Guid root)
        {
            distances[root] = 0;
            paths[root] = [Nodes[root].Id];
            counts[root] = BigInteger.One;
            queue.Enqueue(root);
        }
        while (queue.TryDequeue(out var from))
        {
            foreach (var to in Links[from].OrderBy(k => Nodes[k].Id, StringComparer.Ordinal))
            {
                var distance = distances[from] + 1;
                var path = paths[from].Append(Nodes[to].Id).ToArray();
                if (!distances.TryGetValue(to, out var prior))
                {
                    distances[to] = distance;
                    paths[to] = path;
                    counts[to] = counts[from];
                    queue.Enqueue(to);
                }
                else if (prior == distance)
                {
                    counts[to] += counts[from];
                    if (ComparePath(path, paths[to]) < 0) paths[to] = path;
                }
            }
        }
        var ordered = Nodes.Values.OrderBy(n => n.Id, StringComparer.Ordinal).ToArray();
        var principals = ordered.Where(n => !n.Group && paths.ContainsKey(n.Uuid)).Select(n =>
            new PrincipalEvidence(n.Id, n.Uuid, n.Dn, n.Display, Array.AsReadOnly(paths[n.Uuid]), counts[n.Uuid].ToString(CultureInfo.InvariantCulture))).ToArray();
        var groups = ordered.Where(n => n.Group).Select(n => new GroupEvidence(n.Id, n.Uuid, n.Dn,
            Array.AsReadOnly(paths.GetValueOrDefault(n.Uuid) ?? []))).ToArray();
        var edges = Links.SelectMany(p => p.Value.Select(to => new MembershipEdge(Nodes[p.Key].Id, Nodes[to].Id)))
            .OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.To, StringComparer.Ordinal).ToArray();
        var complete = errors.Count == 0 && before is not null && before == after && before.Revision % 2 == 0;
        return new Resolution("0.1.0", complete, Array.AsReadOnly(errors.ToArray()), Array.AsReadOnly(principals),
            Array.AsReadOnly(groups), Array.AsReadOnly(edges), Components(),
            Array.AsReadOnly(Missing.OrderBy(m => m.Group, StringComparer.Ordinal).ThenBy(m => m.Dn, StringComparer.Ordinal).ToArray()),
            new ObservationMetadata(before, after, requests, GroupCount, PrincipalCount, ObservedEdges, complete));
    }
    private static int ComparePath(string[] a, string[] b)
    {
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var comparison = StringComparer.Ordinal.Compare(a[i], b[i]);
            if (comparison != 0) return comparison;
        }
        return a.Length.CompareTo(b.Length);
    }
    private IReadOnlyList<IReadOnlyList<string>> Components()
    {
        var next = 0;
        var indices = new Dictionary<Guid, int>();
        var low = new Dictionary<Guid, int>();
        var stack = new Stack<Guid>();
        var onStack = new HashSet<Guid>();
        var components = new List<IReadOnlyList<string>>();
        void Visit(Guid v)
        {
            indices[v] = low[v] = next++;
            stack.Push(v);
            onStack.Add(v);
            foreach (var w in Links[v].Where(w => Nodes[w].Group).OrderBy(w => Nodes[w].Id, StringComparer.Ordinal))
            {
                if (!indices.ContainsKey(w)) { Visit(w); low[v] = Math.Min(low[v], low[w]); }
                else if (onStack.Contains(w)) low[v] = Math.Min(low[v], indices[w]);
            }
            if (low[v] != indices[v]) return;
            var members = new List<Guid>();
            Guid member;
            do { member = stack.Pop(); onStack.Remove(member); members.Add(member); } while (member != v);
            if (members.Count > 1 || Links[v].Contains(v))
                components.Add(Array.AsReadOnly(members.Select(k => Nodes[k].Id).Order(StringComparer.Ordinal).ToArray()));
        }
        foreach (var group in Nodes.Values.Where(n => n.Group).OrderBy(n => n.Id, StringComparer.Ordinal))
            if (!indices.ContainsKey(group.Uuid)) Visit(group.Uuid);
        return Array.AsReadOnly(components.OrderBy(c => c[0], StringComparer.Ordinal).ToArray());
    }
}
