# Development 0.1.0 contract

This document defines the supported subset, not general LDAP compliance. Public observations never grant access or authenticate a user.

## Public surface

`SyntheticFixture.Create(token)` creates a fresh, owned synthetic directory on unprivileged Linux. Root is `cn=root,ou=groups,dc=example,dc=test`.

- `ResolveGroup(rootDn, limits, token)` returns `Task<Resolution>`.
- `ResolveAndExport(rootDn, limits, token)` additionally attempts guarded immutable publication.
- `Apply(FixtureMutation, token)` performs one of three fixed synthetic mutations through the cooperative writer.
- `Oracle(token)` runs the independently implemented system ldapsearch client with fixed arguments.
- `ReadEvidence(fileName, token)` returns bytes only for a generation published by this fixture instance.
- `DisposeAsync()` stops and reaps the retained server before deleting owned state. It waits for an in-progress public operation. Do not abandon a fixture without disposing it.

One fixture serializes its own public operations. The filesystem flock protocol also serializes cooperating writers and final export checks across process boundaries. The monotonic observation budget starts before waiting for this instance gate and continues through LDAP traversal and publication checks. A queued observation honors cancellation immediately and returns incomplete CANCELLED, or DEADLINE when its admission budget expires, with zero LDAP requests and no publication. Setup, writers, oracle, evidence reads and disposal are not traversal operations.

The library exposes no arbitrary endpoint, command, LDAP modification, filesystem export destination or credential API.

## Supported entries and identity

Groups have only the object classes top/groupOfNames, one cn fixture identifier, and nonempty member values. Principals use the top/person/organizationalPerson/inetOrgPerson subset with uid and cn equal to the fixture identifier, sn exactly `Synthetic`, and one displayName. Identifiers contain 1–64 ASCII letters, digits, `_` or `-`; identifiers must be unique among discovered UUID identities. Unknown entry types are errors, not principals. Operational attributes are supplied by OpenLDAP.

DN equality belongs to the server. References are strictly base-object reads with referrals and alias dereferencing disabled. The fixed read filter is supplied by the maintained client; no caller value is interpolated into a filter. The oracle has a fixed `(objectClass=groupOfNames)` filter. There is consequently no application-written filter escaping implementation.

Each lookup is deduplicated by entryUUID. An ordinal exact-DN cache only avoids redundant requests. Returned DNs/display strings are retained without case folding or Unicode normalization. For multiple equivalent references to one UUID, the first fetched server display string is retained. This choice does not affect identity or path ordering.

Entry decoding permits at most 4,097 member values, 16 values for each other selected attribute, and 2,048 UTF-16 code units per value or returned DN. These checks happen after the LDAP client has decoded its response; they are not a bound on all native/network allocations.

## Traversal and explanations

Traversal proceeds in bounded breadth-first group waves, sorted by ordinal fixture identifier. Requests within a wave are sequential, never concurrent. Member DN strings are dispatched in ordinal order; this dispatch order is not DN equality.

Resolved adjacency is a set of UUID pairs. `observedEdges` counts processed declared references, including missing references and references whose lookup hits a cache. A limit is checked before consuming the next edge. Known resolved links are retained when traversal stops.

Shortest paths are measured in membership edges. The returned path includes both root and principal, so the original path root/G1/B has three identifiers and exactly two edges. Equal-length paths use lexicographic ordinal ordering of the identifier sequence. `shortestPathCount` is an exact decimal integer counting distinct shortest UUID-edge paths; it does not count arbitrarily long walks. Cyclic graphs can have infinitely many walks without making this count infinite.

`cyclicComponents` lists strongly connected group components of size greater than one, plus singleton groups with a self-loop. Components and identifiers are ordinally sorted. It does not enumerate every cycle. An incomplete observation can only describe the discovered subgraph; further undiscovered components may exist.

## Limits and partial results

Defaults and ceilings are 256 groups, 1,024 principals, 4,096 observed edges, 32 **total** LDAP requests and 2,000 monotonic milliseconds. Custom limits must be positive and cannot exceed these ceilings. The request cap includes starting/final guard reads, every base-object cache miss and every missing lookup. TCP connection establishment is not an LDAP request. No bind is issued by the observer.

The 32-request cap can win long before the group or principal cap. No full 256-group traversal is promised. Every dispatch and every awaited source operation is checked before and after for cancellation/deadline. Client time limits are remaining milliseconds; LDAP server search time limits are one second. No abandoned background LDAP operation is created by wrapping it in WaitAsync. An LDAP failure before the monotonic deadline is LDAP_ERROR, even if it was the client's earlier timeout.

Incomplete observations retain known principals, groups, adjacency, unresolved references and metadata. Error codes are sorted unique strings:

- GROUP_CAP, PRINCIPAL_CAP, EDGE_CAP, REQUEST_CAP, DEADLINE, CANCELLED
- MISSING_REFERENCE, INVALID_ROOT, INVALID_ENTRY, UNSUPPORTED_ENTRY, DUPLICATE_IDENTIFIER, ENTRY_LIMIT
- LDAP_ERROR, INVALID_GUARD, ODD_REVISION, REVISION_CHANGED, LOCK_ERROR
- PUBLICATION_ERROR, EVIDENCE_LIMIT

More than one error may be present, for example GROUP_CAP followed by REQUEST_CAP while trying to read the final guard. Missing root is INVALID_ROOT; missing member is an explicit unresolved reference. Invalid caller arguments throw argument exceptions. Fixture/process/setup failures use sanitized FixtureException codes. Raw LDAP messages, matched DNs and native stderr are not public diagnostics. Observation data intentionally contains synthetic DNs.

## Revision protocol

The applicationProcess guard at `cn=revision,dc=example,dc=test` has exactly two description values: `epoch=<canonical nonempty GUID>` and `revision=<canonical nonnegative Int64 ASCII decimal>`. No leading zeroes except `0`, signs, whitespace, duplicate fields or overflow are accepted.

The offline seed starts at revision 2 with a fresh random source epoch. A single cooperating writer:

1. Acquires exclusive filesystem flock on writer.lock.
2. Reads an even guard and rejects insufficient Int64 headroom.
3. Replaces guard descriptions with the same epoch and revision + 1, making it odd.
4. Performs its declared membership LDAP modify operation.
5. Replaces the guard with revision + 2, making it even, then unlocks.

Counter arithmetic does not wrap. Writer cancellation/failure after step 3 leaves the guard odd. There is no rollback guess, commit guess, timeout-based repair or automatic conversion to even. Dispose and create a new fixture from the declared seed; the new source epoch prevents an ABA reset when revision returns to 2. All writers, including tools outside this API, must cooperate. The ACL does not itself enforce flock or revision semantics.

A reader records the initial guard, traverses, and reads the final guard. Initial odd revision blocks traversal. Changed epoch/revision or odd final revision means REVISION_CHANGED. Equal even guards plus no other error give `complete=true` and `qualifiedStable=true`. These terms mean complete within this owned synthetic schema under the every-writer-cooperates assumption, not an LDAP transaction or general snapshot guarantee.

## Publication and comparison

For export, final guard recheck and publication hold the cooperative writer flock. Only complete qualified observations can create evidence JSON. An observation with missing members, any exhausted cap, cancellation, malformed guard or changed revision produces zero generations.

JSON is UTF-8, camelCase, indented, contractVersion 0.1.0. Arrays are deterministically ordered. UUIDs, returned strings, source epoch/revision, counts and the graph are retained. Publication metadata itself is excluded from evidence bytes. Evidence is capped at 4 MiB.

A generation filename contains epoch, revision and SHA-256 of the serialized observation. A private temporary file is flushed, the budget is checked immediately before commit, and same-filesystem no-replace rename publishes it. Existing identical generations are reused with `created=false`; mismatched bytes fail. No append, overwrite, partial evidence file or persistent counter JSON is used. The rename is the publication linearization point; no deadline failure is introduced after successful commit. Directory fsync, crash recovery and durable signatures are not provided. Staging files are not generations.

`MembershipDifference.Compare` requires complete observations with equal source epochs. It reports additions/removals by entryUUID and returns their fixture identifiers in ordinal order. Renaming an existing identity is not an addition/removal. Comparisons across rebuilds are rejected.

## Native ownership

The original helper has four fixed modes and absolute system-tool paths. It resets SIGCHLD to SIG_DFL with flags zero before fork, retains the unreaped direct child, observes exit with waitid WNOWAIT, finishes TERM/250 ms grace/KILL signaling, then calls waitpid. Signals never target a reaped/reused PID. The managed layer controls the owner only through STOP/EOF on a private pipe and awaits/drains it; it never uses Process.Kill.

Only the single-threaded owner's child arms Linux PDEATHSIG against that owner. The owner does not arm it against a .NET launching thread. A parent-process exit closes the control pipe. Fixed CLI children have a five-second run limit; server lifetime is controlled by its owner pipe. Native stdout is retained only for oracle output, capped at 1 MiB; other stdout and stderr have 64 KiB limits and are discarded. Exceeding a cap terminates the child and produces a sanitized failure. Managed drains are bounded too. Kernel scheduling and uninterruptible kernel waits preclude an absolute shutdown latency guarantee.
