using System.Text;

namespace MembershipEvidence;

internal static class Seed
{
    internal const string RootDn = "cn=root,ou=groups,dc=example,dc=test";
    internal const string G1Dn = "cn=G1,ou=groups,dc=example,dc=test";
    internal const string G2Dn = "cn=G2,ou=groups,dc=example,dc=test";
    internal static string Person(string id) => $"uid={id},ou=people,dc=example,dc=test";
    internal static string Ldif(string epoch)
    {
        var text = new StringBuilder("dn: dc=example,dc=test\nobjectClass: top\nobjectClass: domain\ndc: example\n\n");
        foreach (var ou in new[] { "groups", "people" })
            text.Append($"dn: ou={ou},dc=example,dc=test\nobjectClass: organizationalUnit\nou: {ou}\n\n");
        text.Append($"dn: {Resolver.GuardDn}\nobjectClass: applicationProcess\ncn: revision\ndescription: epoch={epoch}\ndescription: revision=2\n\n");
        foreach (var id in new[] { "A", "B", "C", "D" })
        {
            var display = Convert.ToBase64String(Encoding.UTF8.GetBytes($"Principal {id} sintético"));
            text.Append($"dn: {Person(id)}\nobjectClass: top\nobjectClass: person\nobjectClass: organizationalPerson\nobjectClass: inetOrgPerson\nuid: {id}\ncn: {id}\nsn: Synthetic\ndisplayName:: {display}\n\n");
        }
        foreach (var group in new[] { ("root", new[] { G1Dn, G2Dn }), ("G1", new[] { Person("A"), Person("B") }), ("G2", new[] { Person("B"), Person("C") }) })
        {
            text.Append($"dn: cn={group.Item1},ou=groups,dc=example,dc=test\nobjectClass: top\nobjectClass: groupOfNames\ncn: {group.Item1}\n");
            foreach (var member in group.Item2) text.Append($"member: {member}\n");
            text.Append('\n');
        }
        return text.ToString();
    }
    internal static string Revision(Guard guard) => $"dn: {Resolver.GuardDn}\nchangetype: modify\nreplace: description\ndescription: epoch={guard.Epoch}\ndescription: revision={guard.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n-\n\n";
    internal static string Mutation(FixtureMutation mutation) => mutation switch
    {
        FixtureMutation.AddCycle => $"dn: {G2Dn}\nchangetype: modify\nadd: member\nmember: {RootDn}\n-\n\n",
        FixtureMutation.AddMissingReference => $"dn: {G1Dn}\nchangetype: modify\nadd: member\nmember: uid=missing,ou=people,dc=example,dc=test\n-\n\n",
        FixtureMutation.ReplaceG2WithD => $"dn: {G2Dn}\nchangetype: modify\nreplace: member\nmember: {Person("D")}\n-\n\n",
        _ => throw new ArgumentOutOfRangeException(nameof(mutation))
    };
}
