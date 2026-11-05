# Declared synthetic seed

Base: `dc=example,dc=test`. Containers: `ou=groups` and `ou=people`. No real people or organizations are represented by the entries.

| Group | Initial member identifiers |
| --- | --- |
| root | G1, G2 |
| G1 | A, B |
| G2 | B, C |

A, B, C and D are inetOrgPerson entries with uid/cn equal to the letter, sn `Synthetic`, and UTF-8 displayName `Principal <letter> sintético`. D is initially unreferenced. OpenLDAP assigns entryUUIDs; the seed does not invent operational UUID attributes.

There are exactly three groups, six total member links, and four links below G1/G2. Reachable principals are A, B and C. B has two shortest paths of length two; the ordinal identifier tie-break selects root/G1/B.

The applicationProcess guard starts at revision 2 with a newly generated epoch. Each successful fixed mutation increments the revision by two:

- AddCycle adds root to G2. The only cyclic SCC is {G2, root}; G1 is not cyclic.
- AddMissingReference adds `uid=missing,ou=people,dc=example,dc=test` to G1. Known principals remain A/B/C, but the observation is incomplete with one unresolved reference.
- ReplaceG2WithD replaces all G2 member values with D in one LDAP modify operation. On a fresh seed, reachable principals become A/B/D: one addition and one removal.

These are fixed mutations, not an arbitrary LDIF interface. Repeating an add operation can fail with a duplicate-value error; the writer then deliberately leaves the revision odd rather than assuming the batch succeeded. Dispose and create a fresh seed to recover.

The independent oracle is `/usr/bin/ldapsearch` with fixed groupOfNames filter, base, attributes, local URI, anonymous reads, one-second server search limit and 256 result limit. It returns group/link counts, not an alternative client-side DN matching implementation. It has no access to the resolver's graph or cache.
