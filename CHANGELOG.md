# Changelog

## 1.3.0 - 2026-10-23

Added temporary SQLite investigation caches, historical paging beyond the former
20,000-result cap, full-query scanning with progress/cancellation, atomic all-match
JSON / CSV-bundle exports with coverage metadata, and native source EVTX export.
Added five guided investigations with full-query evidence summaries; provider-scoped
selectors; event version/activity/process metadata; guarded future authentication
schemas; additional plain-language recorded-fact explanations and application
report/runtime/hang guidance. Live sources now retry, resume accepted bookmarks,
warn on stale-checkpoint backfill, suppress recent replay duplicates and support
explicit overflow recovery. Session checkpoints are not persisted across restarts.
Expanded native reference comparison and synthetic scale validation; see validation.md.
Domain-controller and sustained native live testing remain environment-specific.

## 1.2.0 - 2026-10-22

Integrated Microsoft troubleshooting, Ultimate Windows Security and EventSentry
references. Added a Help tab with offline, provider-scoped investigation guides,
conditional code-specific checks/resolutions, related evidence and applicability.
Added 389/382 indexed Windows audit-event page links, code lookup, source directory
and controlled browser opening. Reports/copy include guidance and attribution.
Unmapped events explicitly state that no specific resolution guide is available.
Third-party articles remain online; event contents are never included in links.

## 1.1.0 - 2026-10-22

Expanded offline Microsoft references to 7,496 code definitions and 342
provider-scoped Security audit labels. Added exact symbols, source references,
explicit ambiguous/unmapped cases and signed-code support. Zero 4625 substatus
no longer suggests successful authentication. Added coverage panel and decoded
code details to reports.

Export events now offers direct CSV/JSON choices. Changed the Fluent accent to
teal-blue and applied severity colors to every event cell, including selection;
warnings are orange.

## 1.0.0 - 2026-10-22

Initial independent implementation: paged local/remote event queries, EVTX,
provider-scoped explanations, account search, lockout patterns, live native
subscriptions, grouping, correlation, portable filters and JSON/CSV reports.
Remote/domain-controller behavior is implemented but requires environment-specific
validation. See docs/validation.md for verified coverage and limitations.
