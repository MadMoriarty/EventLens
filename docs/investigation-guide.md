# EventLens 1.3 workflow guide

## Start with a question

1. Choose computers/channels with **Sources**, or open exported **EVTX** files.
2. Choose a time range. Start narrow enough to investigate comfortably.
3. Use **Guided investigations** for a lockout, failed sign-in, application crash,
   service failure or unexpected restart. Click **Run this investigation**.
4. Select an event. **Explain** states recorded facts and decodes supported codes;
   **Help** separates possible causes, next checks and conditional fixes. **Original**
   and **XML** retain the underlying evidence.

Guided investigations retain computers and time range but reset conflicting filters.
Non-account questions clear the identity field. Active provider/event selections
are shown above the paging controls. To remove them, reopen Guided investigations
and click **Clear guided event selection**, then Search.

## Search beyond the screen

The table is one page, normally up to 500 matching records. **Next page** continues
the same captured query; **Previous page** revisits its cache. Large individual
records can reduce page size to keep memory bounded.

**Scan all matches** continues through all accessible history in the selected scope.
It reports examined records and cached matches. You can Cancel: completed batches
remain available. An empty batch does not mean no matches exist later in the log.
The historical cache has no fixed record-count ceiling, but needs free disk space.

For a guided question, click **Run this investigation**, then **Analyze all matches**
in the same dialog. It scans the complete captured query and summarizes recorded
counts, expected event types absent from that scope, source errors and next checks.
Use **Copy summary** for investigation notes. The initial dialog provides instructions before searching; the first evidence
summary covers the current page until you request all-match analysis.

The table's counts, column sorting, Groups and Correlate cover the displayed page.
All-match analysis covers the captured query. Neither proves that auditing retained
every event or that a suspected cause is confirmed.

## Choose the right export

Open **Export events**:

| Choice | What it includes |
| --- | --- |
| Displayed page CSV / JSON | Only the rows currently displayed. Ctrl+E does this too. |
| ALL matching events JSON | Every deduplicated match retrievable from the captured query, including records not displayed. Completes scanning automatically. |
| ALL matches CSV bundle | ZIP containing events.csv and investigation.json. The metadata identifies the query, sources, examined/returned counts and completeness. |
| Source native EVTX | One selected source with native time/provider/event-ID/severity filters. Identity, text, origin and category filters are NOT applied. |

Full-query exports preserve original XML and explanations. Repeated copies of the
same original XML record are merged with contributing-source provenance. Source
errors mark coverage incomplete in the report; review them before sharing. Cancel
preserves an existing destination and removes the unfinished report.

A full query captures its upper time bound when Search starts. Windows logs can
still rotate while being read. To include a later interval (including events seen
in Live), run Search again first. Native EVTX exports do not bundle foreign
provider message resources. CSV/JSON preserve the rendered text that was available.

## Live monitoring and recovery

**Watch User / Live** starts subscriptions with the selected filters. Pause stops
display updates while bounded collection continues. Filters change only on restart.

Transient source errors retry automatically. Retained events after an accepted
bookmark replay on reconnection. An unusable checkpoint triggers a visible gap
warning and a two-minute timestamp overlap. This cannot reconstruct overwritten
records or guarantee recovery when clocks are wrong.

If the queue overflows, collection stops visibly. **Recover live** resumes display
and requests replay from the accepted checkpoints. Recent replay duplicates are
suppressed within a bounded window. Review source/gap notices even after recovery.
Stop/start can reuse matching checkpoints during the current app session. Closing
EventLens discards checkpoints; this is not an unattended recording service.

## Privacy and limits

Searches create temporary SQLite files under your user temporary directory in
EventLens-QueryCache. They hold original event evidence and are removed on normal
query disposal. A crash/forced exit can leave files; treat them as private logs.
Exported reports may contain usernames, hostnames and IPs. Nothing is uploaded by
the app, and reference links never include that event content.

A code definition is not a complete diagnosis. Unknown events retain provider
messages/XML. Unsupported future versions of the reviewed authentication schemas
withhold old payload-sensitive interpretation. A logon success, ticket request and
successful business operation are different things.

Remote/DC access uses your current Windows rights. Domain aliases are not guessed;
use exact recorded names or SIDs and include all relevant controllers manually.
Real domain-controller and sustained native live validation are still required.
