using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace MembershipEvidence;

public sealed record Limits(int Groups = 256, int Principals = 1024, int Edges = 4096,
    int Requests = 32, int Milliseconds = 2000)
{
    internal void Validate()
    {
        if (Groups is < 1 or > 256 || Principals is < 1 or > 1024 || Edges is < 1 or > 4096 ||
            Requests is < 1 or > 32 || Milliseconds is < 1 or > 2000)
            throw new ArgumentOutOfRangeException(nameof(Limits), "Limits may only lower the documented ceilings.");
    }
}

public sealed record Guard(string Epoch, long Revision);
public sealed record PrincipalEvidence(string Id, Guid EntryUuid, string Dn, string DisplayName,
    IReadOnlyList<string> Path, string ShortestPathCount);
public sealed record GroupEvidence(string Id, Guid EntryUuid, string Dn, IReadOnlyList<string> Path);
public sealed record MembershipEdge(string From, string To);
public sealed record UnresolvedReference(string Group, string Dn);
public sealed record ObservationMetadata(Guard? Before, Guard? After, int Requests,
    int GroupCount, int PrincipalCount, int ObservedEdges, bool QualifiedStable);
public sealed record Publication(string FileName, bool Created);
public sealed record Resolution(string ContractVersion, bool Complete,
    IReadOnlyList<string> Errors, IReadOnlyList<PrincipalEvidence> Principals,
    IReadOnlyList<GroupEvidence> Groups, IReadOnlyList<MembershipEdge> Edges,
    IReadOnlyList<IReadOnlyList<string>> CyclicComponents,
    IReadOnlyList<UnresolvedReference> Unresolved, ObservationMetadata Observation,
    [property: JsonIgnore] Publication? Publication = null);
public sealed record MembershipDifference(IReadOnlyList<string> Added, IReadOnlyList<string> Removed)
{
    public static MembershipDifference Compare(Resolution before, Resolution after)
    {
        if (!before.Complete || !after.Complete || before.Observation.Before?.Epoch != after.Observation.Before?.Epoch)
            throw new ArgumentException("Comparison requires complete observations from the same source epoch.");
        var a = before.Principals.ToDictionary(p => p.EntryUuid, p => p.Id);
        var b = after.Principals.ToDictionary(p => p.EntryUuid, p => p.Id);
        return new(Array.AsReadOnly(b.Where(p => !a.ContainsKey(p.Key)).Select(p => p.Value).Order(StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(a.Where(p => !b.ContainsKey(p.Key)).Select(p => p.Value).Order(StringComparer.Ordinal).ToArray()));
    }
}
public sealed record OracleReport(int Groups, int TotalLinks, int BelowRootLinks);
public enum FixtureMutation { AddCycle, AddMissingReference, ReplaceG2WithD }
public sealed class FixtureException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
internal sealed class ObservationFailure(string code) : Exception(code)
{
    internal string Code { get; } = code;
}
public static class EvidenceJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
