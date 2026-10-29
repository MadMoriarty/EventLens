# EventLens user guide

## Install and open

Use Windows x64 on a .NET 10 supported Windows version. If a repository release has a portable ZIP attached, extract the whole package and run `EventLens.exe`. Otherwise, follow [the build guide](build.md). Portable builds include .NET, require no installer, and are unsigned; Windows SmartScreen may warn.

Start as a standard user. EventLens reads only sources your account can access. It does not grant Security-log rights or modify Windows permissions.

## First investigation

1. Select **Sources** and choose computer/channel names, or import an EVTX file. Add relevant domain controllers explicitly when needed.
2. Choose a time range. The default is 24 hours; a narrow interval is easier to investigate.
3. Enter an exact recorded username, domain-qualified name, UPN, or SID for account questions. Refine with event/provider and text filters.
4. Click **Search**, select a record, and inspect **Explain**, the provider message, payload, and original XML.
5. Use **Guided investigations** for account lockouts, failed sign-ins, application crashes, service failures, or unexpected restarts.
6. Use **Next page** or **Scan all matches** to continue beyond the displayed page. **Analyze all matches** gives a guided evidence summary for the captured query.

Interpretations separate recorded facts from possible causes. Actor and affected account are different roles. Missing SIDs, duplicate names across domains, clock skew, source errors, retention, and unsupported event versions limit conclusions. Inspect the original evidence.

## Export the intended scope

The displayed page normally contains up to 500 matches; large individual records can reduce it. Historical paging uses a temporary SQLite cache. A query captures its upper time bound when Search starts; run Search again to include newer events.

| Export | Contents |
| --- | --- |
| Displayed page CSV/JSON | Current page only. Ctrl+E uses this scope. |
| ALL matching events JSON | All retrievable matches, query/completeness metadata, and original XML. |
| ALL matches CSV bundle | events.csv and investigation.json in one ZIP. |
| Source native EVTX | One source with native time/provider/ID/severity filters. Identity/text/origin/category application filters are not applied. |

Review completeness metadata and source errors. Cancellation preserves a previous destination. CSV formula-leading cells are neutralized; JSON retains raw evidence. Neither export is anonymized. See [detailed workflows](investigation-guide.md).

## Live monitoring

**Watch User / Live** captures filters when started. Pause freezes display while bounded collection continues. Changing filters requires stop/start. **Recover live** requests replay from accepted checkpoints after an overflow or interruption.

Bookmarks recover only events still retained by a source. Stale checkpoints produce visible gap warnings and timestamp-overlap replay. Overwritten events cannot be recreated, checkpoints do not survive app restarts, and display/queue limits can evict records or stop collection. This is not an unattended permanent recorder or a zero-loss pipeline.

## References and saved searches

**References & troubleshooting**, **Help**, and **Troubleshooting & references** supply offline guidance and external article links. Links open in your browser when selected and do not include event contents in their URLs.

Saved searches are local and can contain account/source names. Relative time ranges recalculate when run; custom UTC bounds stay fixed. Imported file paths are not saved in reusable filters.

## Troubleshooting

| Problem | Check |
| --- | --- |
| Security-channel access denied | Your current Windows rights and channel permissions. |
| Remote source unavailable | Exact computer name, current account rights, RPC connectivity, firewall/domain policy. |
| Missing provider message | Local rendering resources may be absent; inspect XML. EVTX exports do not bundle provider resources. |
| Few/no matches | Interval/time zone, selected sources, identity role/domain, provider, retention, and access errors. |
| Incomplete live interval | Read gap/overflow notices and query historical logs if retained. |
| Large query uses disk | Cache holds original matches; dispose normally and handle crash leftovers privately. |

## Remove the app

Close EventLens, preserve wanted reports, then delete the portable folder and any fallback `%LOCALAPPDATA%\EventLens` settings. Crash leftovers may remain under `%TEMP%\EventLens-QueryCache`; close the app before removing them. No monitoring service or scheduled task is installed. See [permissions and privacy](permissions-and-privacy.md).
