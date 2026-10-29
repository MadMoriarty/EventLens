# Architecture decision and upstream review

Reviewed 2026-10-22. EventLogExpert commit:
`07a5a286b62a87e0181d18731f6dab0f52870610`.

## EventLogExpert

Microsoft's MIT-licensed project separates Eventing, Runtime, Filtering, platform
services and UI. Its current interface is MAUI/Blazor, with MSIX distribution.
The native reader uses EvtQuery/EvtNext, batches, bookmarks and provider metadata.
The live service buffers updates and carefully disposes watchers outside its
callback lock. Filtering has a parser/lowering layer; multiple logs can be
combined. Provider databases help render foreign-machine event messages. These
are valuable design references, especially cancellation, bounded batches, newest
bookmark semantics and watcher teardown. Its provider descriptions are not a
complete payload-sensitive account-investigation rule engine.

EventLens uses the supported System.Diagnostics.Eventing.Reader implementation
rather than copying the native wrapper or forking the application. This keeps
the requested Avalonia interface, portable distribution and Windows 10 scope
independent of its MAUI/WinUI/MSIX stack. No upstream implementation source or
provider database is embedded. The upstream MIT notice is included for reference.

## EventLens boundaries

- Core: safe XML parsing, normalized identities, provider-scoped interpretation,
  deterministic status mappings, filtering, grouping, temporal correlation/export.
- Windows: EventLogReader cursors, native XPath construction, EventLogSession
  current-credential remote connections, EventLogWatcher and portable JSON.
- App: Avalonia MVVM presentation and platform file/clipboard interactions.
- Tests: synthetic documented event schemas, no access to real logs required.

Historical cursors retain position between pages, merge newest heads and stop
after 500 matches or 25,000 native-filtered examined records per page. Table rows
are virtualized; loaded results are limited to 20,000 rows / 128 MB estimated
payload, and the live queue to 4,096 matches / 64 MB estimated payload. This is a bounded
investigation window, not a full-log index. Grouping and sorting cover loaded rows.

Time, event IDs, exact provider and level use native XPath where supported.
Case-insensitive account matching and arbitrary text require scanning payloads
and provider messages. Username predicates are deliberately not pushed as exact
case-sensitive native predicates, which could silently omit case variants.
Native BCL read batches are 16, within pre-Windows-11 EvtNext buffer constraints.

Historical reads share four native worker leases. UI waits have deadlines and
cancel requests, but Windows RPC initialization cannot always be interrupted;
retired reads release resources when they return. No remote services are created.
Live connections use separate subscriptions and a 4,096-match bounded channel.
Overflow is counted, displayed and stops collection; paused display continues
bounded collection. There is no claim of guaranteed zero loss across disconnects.
History and live subscriptions have separate start times; reload history to
cover the interval before watching began. There is no silent bookmark replay.

Identity correlation prefers usable SIDs. Fallback keys include recorded domain
or computer scope. Bare names across domains stay ambiguous; NetBIOS and DNS
aliases are not assumed. Missing-domain events cannot establish a match for a
domain-qualified query. SID-less events cannot establish a SID query match.

The read-only DataGrid is the stable MIT Avalonia 11 package (11.3.13), compatible
with Avalonia 11.3.22. Newer Avalonia 12 recommends TableView; the pinned stable
11 implementation avoids a commercial control dependency and still virtualizes
rows. The UI does not instantiate one row control per loaded record.

## Primary references

- https://github.com/microsoft/EventLogExpert
- https://learn.microsoft.com/windows/win32/wes/consuming-events
- https://learn.microsoft.com/dotnet/api/system.diagnostics.eventing.reader.eventlogreader
- https://learn.microsoft.com/dotnet/api/system.diagnostics.eventing.reader.eventlogwatcher
- https://learn.microsoft.com/windows/security/threat-protection/auditing/event-4625
- https://learn.microsoft.com/windows/security/threat-protection/auditing/event-4740
- https://learn.microsoft.com/windows/security/threat-protection/auditing/event-4771
- https://learn.microsoft.com/windows/security/threat-protection/auditing/event-4776
- https://learn.microsoft.com/windows/security/threat-protection/auditing/event-4723
- https://learn.microsoft.com/windows/security/threat-protection/auditing/event-4724
- https://learn.microsoft.com/openspecs/windows_protocols/ms-erref/
- https://docs.avaloniaui.net/controls/data-display/structured-data/datagrid
- https://dotnet.microsoft.com/platform/support/policy/dotnet-core

## 1.3 architecture update (supersedes historical/display limitations above)

QuerySession now retains deduplicated source records in a private temporary SQLite
ResultStore. Native scans remain bounded per pass; CompleteAsync exhausts available
history. ReadAllAsync completes capture before streaming cached pages so duplicate
provenance is final. SQLite transactions preserve consumed batches on cancellation;
no historical record-count ceiling is imposed by the view. Cache pages and native
batches also have byte budgets. The live table remains bounded separately.

Streaming reports write to a sibling temporary file and atomically replace the
chosen destination only after success. Full CSV exports bundle data and coverage
metadata in one ZIP. Native EVTX exports apply only native predicates and never
replace an imported input. Guided provider/event selectors are checked natively
and again by the client. Authentication schema guards prevent future versions from
silently inheriting reviewed payload-sensitive interpretations.

Live monitoring uses an injectable native subscription adapter and supervised
reattachment with per-source accepted bookmarks, bounded replay deduplication,
source epochs and overflow recovery. Stale/no-checkpoint recovery explicitly warns
before timestamp-overlap replay. No checkpoints are persisted across app restarts.
Real remote/high-rate/native rotation behavior still requires lab validation.
