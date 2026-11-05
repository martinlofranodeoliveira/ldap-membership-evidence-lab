# Design

## Separate observation from authority

The library only creates its own synthetic directory. It cannot be pointed at an employee directory, accept a password or issue an authorization decision. A principal in a result is an observed graph vertex, not an authenticated identity. The useful artifact is an explanation with explicit incompleteness.

## Four boundaries

1. **Native ownership.** The small C executable is the sole reaper of slapd or one fixed CLI tool. This avoids treating a numeric PID obtained after Process.Start as ownership. SIGCHLD disposition is reset before fork, including inherited automatic-reap flags. WNOWAIT exit observation preserves child identity until every possible signal has finished. EOF represents managed-parent process lifetime without Linux's launching-thread PDEATHSIG trap.
2. **LDAP source.** Novell 4.0.0 connects only to the owned loopback port, reads base objects, disables referrals/aliases and applies remaining millisecond timeouts. A budget preflight is necessary because an already-cancelled token is not alone a reliable pre-dispatch veto. Exceptions are converted to codes without exposing directory error text.
3. **Graph resolver.** A raw-DN cache saves requests; only entryUUID establishes identity. The resolver discovers bounded group waves and records UUID edges, then separately analyzes shortest paths and SCCs. Analysis does not depend on the order in which equivalent DNs happened to be requested.
4. **Evidence publication.** The every-writer-cooperates protocol supplies a narrow consistency qualification. A writer's flock covers its odd/mutate/even sequence. A reader does not lock the whole traversal, but locks final recheck and commit. Missing references, limits and guard failures never become evidence generations.

## Why not implement DN parsing?

RFC 4514 display syntax is not enough to reproduce server schema matching rules. The server already owns DN matching. A reference such as `CN=G\31, OU=groups,DC=example,DC=test` is sent as a base DN and resolves to the same entryUUID as the ordinary spelling. The client preserves returned display strings rather than inventing normalized ones. Filter values are never constructed from caller data; all filters are constants.

## Graph choices

Resolved links are UUID sets. Breadth-first analysis minimizes edge count. A dynamic count over BFS distance layers gives the number of shortest paths without attempting to count infinite cyclic walks. Path sequences are explicitly compared with StringComparer.Ordinal, so B chooses root/G1/B even if G2 was discovered first. Tarjan analysis is restricted to at most 256 group vertices; bounded recursion reports SCCs, not an enumeration of cycles. Sorted read-only collection wrappers prevent accidental list mutation through the returned interfaces.

## Guard failure and ABA

An odd revision means a batch may be incomplete, even if the final membership happens to resemble an expected set. No inference repairs it. A new offline seed gets a fresh epoch; even revision 2 from that seed cannot equal revision 2 from the abandoned source. Revision increments are checked before making the guard odd. The protocol has no validity when an independent writer bypasses the lock/guard convention. Local peer-credential ACLs restrict fixture write access but do not magically enforce cooperation.

## Storage and process lifecycle

The fixture creates a random private 0700 tree with MDB storage, LDAPI socket, fixed input files, guard lock and evidence directory. An ephemeral loopback port is reserved then released before slapd starts. There is a small bind race; startup requires the expected private seed epoch, and an exited owned server fails readiness. Protection from a malicious same-UID local process or port impersonation with knowledge of private state is outside the threat model.

slapadd initializes data while the server is stopped. slapd runs foreground with two threads and a 16 MiB MDB map. Anonymous TCP can read synthetic entries, not modify them. The local LDAPI EXTERNAL identity can manage only this fixture database. All CLI tools get LDAPNOINIT=1 and private HOME/LDAPCONF, so ambient ldaprc settings and referral options are not inputs.

Operations retain the instance gate until they finish. Observation cancellation and the monotonic deadline also cover waiting to enter that gate; one shared budget continues after admission. Disposal waits for the gate, stops/drains/awaits the owner, then recursively removes only the owned tree without following symlinks. Native stderr is discarded, not copied into exceptions. No runtime compiler, installer, shell command construction, remote deployment, or numeric PID fallback exists.

## Reproducibility versus source identity

The demo summary deliberately excludes UUIDs and epochs, making its output deterministic. Genuine evidence includes those values because suppressing source identity would undermine rebuild detection. Evidence bytes are deterministic for the same observed graph, returned display strings, budget accounting and guard. Repeated identical exports reuse a content-addressed immutable generation.

## Tests

Real OpenLDAP tests cover closure, Unicode, real server DN equality, independent ldapsearch counts, cyclic SCCs, missing references, replacement and limits. Scripted boundary tests force revision 2→3, stable revision 4, epoch ABA, malformed guards, ignored cancellation, post-await deadline exhaustion and unsupported schema. A real silent TCP peer qualifies the maintained client's millisecond timeout behavior. Native negative tests exercise a copied executable and sanitize a failed owned seed operation. These tests do not assert that arbitrary LDAP servers provide snapshots.

## Deliberate exclusions

No generalized LDAP administrator, credential broker, employee evaluation, arbitrary schema mapper, transaction wrapper, standards-conformance suite, signed archive, power-loss recovery system or production-readiness claim is provided.

Primary references: [Novell client](https://github.com/dsbenghe/Novell.Directory.Ldap.NETStandard), [OpenLDAP 2.6 administration](https://www.openldap.org/doc/admin26/), [RFC 4511](https://www.rfc-editor.org/rfc/rfc4511), [RFC 4515](https://www.rfc-editor.org/rfc/rfc4515), [RFC 4530](https://www.rfc-editor.org/rfc/rfc4530).
