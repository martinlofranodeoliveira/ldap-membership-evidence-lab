# Security

Development version 0.1.0 is a synthetic local-directory laboratory, not a production directory or authorization component. Only this development line is described here; no support lifetime or response-time SLA is promised.

Report vulnerabilities using [GitHub private vulnerability reporting](https://github.com/martinlofranodeoliveira/ldap-membership-evidence-lab/security/advisories/new). Do not post exploit details, credentials, real directory data or native diagnostic dumps in public issues. For general contact, use [Lofrano Tecnologia's official homepage](https://lofranotecnologia.com.br).

## Intended protections

- No arbitrary remote LDAP endpoint, end-user authentication, password, access-grant operation or real directory credential.
- Anonymous reads only on the owned synthetic loopback listener. Modification uses the private fixture's local peer-credential identity.
- Private owned storage, disabled referral chasing/alias dereferencing, fixed filters and fixed native tool argv.
- Every LDAP dispatch and await is budget checked; incomplete observations cannot become evidence generations.
- Writer flock and source epoch plus nonwrapping odd/even revision guard, assuming every writer cooperates.
- Retained direct-child ownership, bounded output, termination before reap and no Process.Kill or late PID signaling.
- No telemetry, tracking, secrets, runtime package download or runtime compilation.

## Non-guarantees

The guard is not a general LDAP snapshot or transaction mechanism. A writer that bypasses the protocol invalidates the evidence qualification. Local same-UID interference, a compromised kernel/system binary, uninterruptible kernel waits and power-loss durability are outside the threat model. Application graph limits do not bound all server or client decoder allocations. The data are synthetic, but loopback anonymous reads can be visible to other local processes. Do not place real information in this fixture.

An odd revision after failure must not be repaired by guessing. Stop/dispose the owned fixture and create a new directory from the declared seed with a fresh source epoch. Never point cleanup, native tool input or evidence paths at unowned state.
