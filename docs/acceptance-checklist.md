# EventLens 1.3 acceptance checklist

Use a disposable lab and authorized event-reader accounts. EventLens never changes
permissions, audit policy, firewall settings or event logs to manufacture a test.
Keep real EVTX/JSON outputs private. Record OS builds, log/provider versions, exact
source/query/time bounds, results and elapsed times.

| Scenario | Acceptance evidence | Current verification |
| --- | --- | --- |
| One stable EVTX snapshot | Same selected events and original XML as direct Windows reader | Local native snapshot passed: 1,266 records, ordered XML matched; all exported |
| Historical scale | Complete disk capture/replay, paged browsing and bounded memory | 100,000 synthetic records; see qa/scale-benchmark-1.3.json; not a native million-record claim |
| Sparse text/identity matches | Continue beyond an empty 25,000-record scan batch | Automated synthetic cursor regression |
| Overlapping files | Unique original records, combined provenance | Automated query/store regression |
| Cancel scan/export | Cache retains consumed evidence; original output preserved | Automated query/export/view-model regressions |
| Security permission denial | Explicit source issue, no invented complete coverage | Actual current-account denial and fixture regression |
| Authorized Security read | Correct matches versus Event Viewer/Get-WinEvent, provider/version/details | Needs authorized environment |
| Two domain controllers | Same SID, actor/target distinction, differing domain names and clock offsets | Fixtures only; actual domain lab required |
| Live reconnect | Resume accepted bookmark, replay retained records, preserve loss notices | Controlled subscription adapter tests; actual remote outage pending |
| Native live high rates | Delivery, queue overflow, pause, recover and cleanup | Controlled overflow/recovery tests; sustained native delivery pending |
| Clear/rotation during live | Stale checkpoint warning; backfill only retained history | Controlled adapter regression; actual native rotation pending |
| Foreign provider resources | Original XML retained; missing rendered message explicit | Parser tests; additional foreign EVTX matrix pending |
| Accessibility/DPI | Keyboard, scaling, multiple monitors and screen-reader use | Native window inspection; broader acceptance pending |

## Reproduce local checks

Run tools/build.ps1 to test and publish. The public preview contains synthetic data.

EventLens.exe --diagnose <private-folder> reads local System/Application/Security,
exports/reopens EVTX, compares one immutable snapshot to a direct native reader,
tests full JSON export and briefly attaches subscriptions. It never clears/writes
a production event log. Its output contains real local data. Do not publish it.

EventLens.exe --benchmark <output.json> generates 100,000 synthetic events through
a test cursor, captures them to disk and verifies ordered XML during page replay.
This measures that data path, not Event Viewer UI speed or native RPC throughput.

## Fair comparison with Event Viewer

Use identical immutable EVTX inputs, time bounds and provider/event predicates.
Verify result sets and original XML first. Separately time completing the same user
task: find a rejection reason, trace a lockout, identify missing sources, and export
evidence. Record clicks/time/errors and memory for both apps. Native reader timing
alone is not an Event Viewer UI benchmark and does not establish superiority.
