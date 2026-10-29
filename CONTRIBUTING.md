# Contributing

Use .NET 10 SDK and Windows for the desktop/native layers. Run both test projects
and the portable build before proposing changes. Core tests use synthetic event
XML and do not require Event Log permissions. Headless interface tests exercise
real Avalonia controls with explicitly synthetic preview data.

Add provider-specific rule tests for every new interpretation. Keep failure vs
success, account vs actor, SID/domain scope and unknown-code behavior explicit.
Never infer causation from temporal correlation. Keep original XML available.
Treat event data as untrusted and never execute embedded commands or links.

Do not commit real logs, EVTX files, usernames, machine identifiers, credentials,
private screenshots or settings. Use synthetic fixtures; private native diagnostic
outputs belong under ignored qa/private. Large data tests must state their scope.

No telemetry, services, automatic uploads, credential prompts or machine policy
changes should be added as incidental dependencies. Document API/OS limitations.
Any log-writing or permission-changing feature would conflict with this tool's
read-only design and requires a separate explicit design decision.

GitHub publishing and actual release creation are human-controlled. The included
release workflow is manual; do not dispatch it without repository-owner approval.
