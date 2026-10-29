# Feature coverage — 1.3

| Area | Implementation | Limits |
| --- | --- | --- |
| History | Captured time bound, native filters, temporary SQLite cache, previous/next pages and complete scan | Available disk/native access; not a permanently maintained full-text index or transactional log snapshot |
| Scale | No fixed historical match-count ceiling; bounded scan batches/pages and progress/cancel | Native large-EVTX and remote throughput need environment benchmarks |
| Exact full-query exports | Atomic JSON or CSV ZIP with original XML, provenance, filters, counts and completeness | Accessible retained records only; source errors mark coverage incomplete |
| Native EVTX | One source, native time/provider/ID/severity filters, original source preserved | Identity/text/category/origin filters excluded explicitly; foreign message resources not bundled |
| Guided questions | Five provider-scoped workflows, full-query evidence summary, missing evidence/next checks, copy | Counts/coverage do not establish a root cause |
| Details | Plain words, codes, original message/XML, event version/activity/process IDs | Not every provider/schema/version has a curated interpretation |
| Identity | Exact recorded username/domain/UPN/SID and actor/target/member distinctions | No AD directory lookup or automatic domain-alias resolution |
| Live recovery | Retained bookmarks, backoff, stale-checkpoint overlap with gap warning, bounded replay dedup, explicit overflow recovery | Session-only checkpoints, finite queue/display, no zero-loss guarantee; real remote/high-rate validation pending |
| Groups/correlate | Expandable groups and temporal correlations | Displayed page only; guided all-match summary is separate |
| Saved searches | Relative/custom ranges, sources and provider-scoped selectors | Imported file list/workspace layouts not fully persisted |
| Portable/offline | Self-contained x64, no service/account/cloud/telemetry, local references | Unsigned; temporary private caches and runtime extraction |

See investigation-guide.md, acceptance-checklist.md and validation.md.
