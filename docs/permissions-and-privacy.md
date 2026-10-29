# Permissions and privacy

## Windows access

EventLens launches as the current user (`asInvoker`). It reads local/remote Event Logs and imported EVTX files. It does not clear logs, change audit policy, install agents, modify firewall rules, or request/store remote passwords.

| Source | Requirements and limits |
| --- | --- |
| Application/System | Current account needs permission on the selected channel. |
| Security/restricted channels | Additional Windows rights may be necessary; access denial is reported. |
| Remote computer | Current Windows authentication, suitable channel rights/Event Log Readers access, RPC connectivity, and firewall/domain policy. |
| Imported EVTX | Read access to the file; original stays unchanged. Provider resources affect message rendering. |

Configure access through your Windows administrator and local policy. EventLens does not discover domain controllers automatically. Elevation does not bypass remote permissions or recover unretained evidence.

## Local files and sensitive content

Settings use `eventlens.settings.json` beside the executable when writable, otherwise `%LOCALAPPDATA%\EventLens`. Saved searches can contain account names and sources. Settings are not encrypted.

Historical queries store original event data in temporary SQLite caches under `%TEMP%\EventLens-QueryCache`. Normal disposal removes them; crashes/forced exits can leave files. Close EventLens before removing leftovers. Caches rely on ordinary user-directory access controls and are not additionally encrypted.

Exports can include usernames, SIDs, computers, IP addresses, paths, payloads, and original XML. They are neither anonymized nor encrypted. Review before sharing. CSV neutralizes formula-leading cells, but treat imported event content as untrusted.

`--diagnose <folder>` reads real local logs, exports a small System EVTX subset, tests reimport/exports, and briefly attaches subscriptions. Keep its outputs private. Documentation preview modes and headless test fixtures use synthetic events.

## Network and live mode

There is no cloud upload, account login, or telemetry backend. Remote sources create authenticated Windows Event Log RPC traffic. Reference links open external websites only when selected; event contents are not placed in URLs. Those sites have their own privacy policies.

Live subscriptions exist only while the app runs. Bounded queues, display retention, source failures, overwritten logs, stale checkpoints, and clock skew can create gaps. Closing disposes subscriptions; no service or scheduled task is installed.

## Distribution

Portable builds are unsigned and have no automatic updater. Single-file .NET builds extract native libraries to the normal temporary extraction location. Preserve dependency notices and rebuild periodically with maintained dependencies.
