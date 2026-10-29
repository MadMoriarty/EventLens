# Validation records

## Publication check — 2026-10-29

Windows x64, .NET SDK 10.0.402: all 103 core tests and all 25 desktop/UI tests
passed in Release configuration. The first sandboxed test attempt aborted because
the test runner could not connect to its child process; rerunning with normal
process access passed. No real event data was published. Documentation previews
use synthetic fixtures. The historical record below describes earlier validation
and its limits.

## Earlier validation - 2026-10-22

Host: Windows x64, project-local .NET SDK 10.0.402, runtime 10.0.13.
Target: net10.0-windows, self-contained win-x64, Avalonia 11.3.22.

## Automated checks

- 67 core tests: XML/Unicode/UTC, prohibited DTDs, malformed input, provider
  identity gating, NTSTATUS and Kerberos codes, success/failure distinction,
  account/group/policy categories, qualified names/UPNs/SIDs, actor/target roles,
  ambiguity, filters, grouping, duplicate evidence, temporal correlation,
  scheduled-task outcomes, JSON/CSV and spreadsheet-formula escaping.
- 8 Avalonia/native-query tests: actual controls with explicit fixture preview,
  navigation selects the real query channel, programmatic section changes do not
  overwrite source choices, relative saved filters retain source scope, safe XPath
  and regional-calendar-independent UTC query timestamps. Report exports cannot
  replace EVTX inputs, and audit Level 0 is not mislabeled as error severity.
- Synthetic workloads: 200,000 identity/text filter evaluations and grouping
  100,000 unique records. These are in-memory logic checks, not a claim of instant
  searching of arbitrary million-record EVTX files. Both test suites pass.

Additional 1.1 regressions verify the embedded catalog counts, exact symbols,
separate code families, HRESULT wrappers, signed representations, ambiguous
application-defined exit codes, unmapped values, provider-scoped reference labels,
code-detail CSV exports, theme accent and every cell in a selected warning row.

## Actual Windows checks

Native diagnostic read 20 System and 20 Application records, preserving raw XML
and UTC timestamps. Security returned access denied under the current Windows
identity; this is expected and was surfaced without attempting elevation or
changing permissions. A small native System EVTX export was reopened through
the file-reader path (20 records), and CSV/JSON export succeeded. A nonexistent
channel produced an explicit source issue instead of crashing the query.

Two local subscriptions (System/Application) were attached and disposed cleanly.
No naturally generated event arrived during the five-second initial observation,
so that run validates setup/teardown, not live delivery. No production log was
written, cleared or modified to manufacture a subscription test.

The actual portable executable was launched through the interactive Windows
desktop. It displayed 500 real local events. Inspection found a navigation issue;
it was fixed and protected by an interface test. UI refinements include compact
details tabs, bounded row display and explicitly colored live failures/lockouts.
Public interface previews use synthetic, labeled data, not real host/account logs.

Final packaged diagnostic exited 0: 40 local records, 20 EVTX records, CSV/JSON
and two subscription setups passed; the Security denied path remained explicit.
Interactive verification confirmed System-only navigation, and native live start,
pause, resume and stop controls. No event delivery was observed during that brief
UI check, so sustained delivery and overflow are still listed below as unverified.

Private native diagnostic outputs are excluded from Git/source archives. They
contain real local XML/EVTX and should not be published.

## Remaining validation boundaries

- No domain-joined test infrastructure: remote queries/subscriptions and actual
  domain-controller account timelines are implemented but not validated here.
- Security-log success requires a sufficiently authorized account; this session
  only verified the denied path. No Windows rights or firewall settings changed.
- Live delivery under sustained high rates, buffer overflow under actual native
  traffic, log rotation/disconnect recovery and full native RPC failure matrices
  still need environment tests. Buffer overflow is explicitly reported/stopped.
- Large EVTX/native log performance, all Windows versions, foreign message
  resources, Windows Snap/DPI/multi-monitor, assistive technology and long-running
  memory stability have not received a production acceptance matrix.
- The executable is unsigned. No public repository or release has been created.

This is a working initial release with explicit limits, not a certification of
production readiness across Windows/domain environments. See features.md for
implementation boundaries and build.md for reproducible validation commands.

## Version 1.1 update validation

All 75 checks pass (67 core, 8 UI/native-query). A fresh packaged diagnostic read
20 System and 20 Application records, reopened 20 exported EVTX records and
exported CSV/JSON. Security access denial was reported as expected. Native live
subscriptions attached with no natural delivery during the five-second check.
The native window exposes the new Export events button. CSV/JSON report
generation passed; the flyout is present in the automated control checks.
The refreshed public preview is synthetic and shows full-row warning orange,
failure red and teal-blue selection/search. User settings are preserved.

## Version 1.2 reference integration

All 96 automated checks pass (86 core, 10 UI/native-query). Added checks for all
three sources, provider-and-ID scoping, raw status/substatus-based conditional
checks, Kerberos ambiguity, successful authentication/task outcomes, shutdown
and crash causation limits, explicit unmapped guides, safe reference URLs,
absence of event identities/IPs in links, CSV/JSON guide attribution and actual
Help-tab binding updates when selecting a different event. The existing offline
catalog and full-row severity-color checks continue to pass.

Public indexes were fetched to verify 389 Ultimate Windows Security and 382
EventSentry modern Windows audit-event page links. Counts reflect indexed URLs,
not individually crawled articles or copies of their databases. The EventSentry
code-lookup form's published `query` parameter was checked before integration.
Browser navigation is user-initiated through Avalonia's launcher; validation does
not send live event contents to any reference website.

Version 1.2 native diagnostics again read System/Application records, reopened the
exported EVTX subset and generated JSON/CSV successfully. Security access denial
was reported without elevation. The native UI exposes Help and References &
troubleshooting. Synthetic preview auto-query is disabled and regression tested.

## Version 1.3 acceptance - 2026-10-23

The upgraded implementation uses temporary SQLite query caches with bounded page
materialization. Historical scans and all-match exports continue beyond the former
20,000-display-row limit. Full-query exports capture scope, errors and completeness;
CSV data and metadata are packaged atomically together. Native EVTX export is
available with explicitly limited native predicates. Five guided questions now
support bounded all-match evidence summaries and missing-evidence/next-check lists.

New tests cover cache provenance/deduplication, beyond-20k scans, sparse matches
past an empty 25k scan pass, cancellation and continuation, atomic exports, actual
view-model paging and full exports, reachable UI actions, future authentication
schema safeguards and controlled live disconnect/stale/overflow/checkpoint replay.
All 128 automated checks pass (103 core, 25 UI/query/recovery). The delivered
release manifest records the packaged validation.

Actual local comparison: a native-exported immutable System EVTX snapshot contained
1,266 matching events. EventLens captured/replayed all 1,266 with the same ordered
original XML and exported all 1,266. Direct XML-only read took 25 ms; interpretation,
disk capture and replay took 287 ms. This is a completeness check on the same input,
not an Event Viewer UI benchmark or a claim that EventLens is always faster.

The 100,000-event synthetic scale path preserved all ordered XML using a 67.7 MB
cache. A recorded run captured in 2.655 s and replayed in 0.712 s; process
peak working set was 84.8 MB. Results are machine/workload-specific; see
qa/scale-benchmark-1.3.json for the recorded run. Native million-record EVTX, remote
RPC, successful Security/DC reads, sustained native live traffic and actual
rotation/disconnect recovery remain unverified here. Five-second native watcher
checks attached two sources without observing a naturally occurring event.

Checkpoints are session-only. Stale recovery may replay retained events with a
two-minute overlap and explicit gap warning; it does not recover cleared logs or
prove gap-free delivery. The executable remains unsigned. No public GitHub release
was created. See acceptance-checklist.md for the remaining lab matrix.
