# Offline reference coverage

EventLens 1.1 embeds the complete rows parsed from these Microsoft reference
tables on 2026-10-22. Runtime decoding does not require an internet connection.

| Reference | Definitions |
| --- | ---: |
| MS-ERREF NTSTATUS values | 1,799 |
| MS-ERREF Win32 error codes | 2,704 |
| MS-ERREF HRESULT values | 2,928 |
| Microsoft event 4771 Kerberos error table | 65 |
| Appendix L modern Security audit event labels | 342 |

These are 7,496 code definitions plus 342 event labels. Definitions include
aliases: a shared number can have several symbols. This is a documented snapshot,
**not every Microsoft code, event, provider or Windows version**. A label does
not mean a detailed investigation rule exists for that event. Reference text
may contain unexpanded placeholders; EventLens does not invent their values.

For native records, Windows also supplies the provider-rendered message when its
resources are installed. Foreign EVTX files can lack these resources. No app can
deduce undocumented provider semantics or a root cause just from an event ID.

## Accuracy rules

- Event IDs are interpreted with their provider. Security reference titles never
  apply to another provider that happens to use the same number.
- Security 4625/4776 status and substatus use NTSTATUS. Kerberos ticket events use
  Kerberos codes. The raw value, family, exact symbol, reference description and
  source URL appear under **Recorded codes** in the Explain pane.
- Zero 4625 substatus means no additional substatus, not a successful logon.
- Decimal, hexadecimal and signed 32-bit representations are accepted.
- Explicit HRESULT and Win32Error fields use their stated family. HRESULTs can
  unwrap the documented Win32 facility or NTSTATUS mapping.
- Generic Status/ErrorCode/ResultCode fields without an established schema show
  possible reference matches explicitly marked **candidate only**. Task action
  exit codes can be application-defined and are not automatically HRESULTs.
- Security logon types receive separate explanations. Unknown values stay raw
  and explicitly unmapped. Windows audit keywords and actual event outcome remain
  separate from a numeric status. Correlations never establish causation.

Open **References & troubleshooting**, then **Offline code coverage**, at the lower left for all source URLs, counts
and HTML SHA-256 fingerprints. JSON reports include structured decoded codes;
CSV includes explanation, code details and interpretation rule. Both export only
currently loaded records unless exporting a selected event/group.

## Rebuilding the reference

`python tools/update-code-catalog.py --refresh` downloads the five official pages,
validates minimum expected counts, and regenerates the embedded JSON with source
fingerprints. Review the diff and update coverage tests/docs before releasing.
Raw HTML stays in ignored `.research/catalog`; it is not included in a release.
Ordinary app builds use the committed embedded JSON and do not access the web.

Sources:

- [NTSTATUS](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-erref/596a1078-e883-4972-9bbc-49e60bebca55)
- [Win32](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-erref/18d8fbe8-a967-4f1c-ae50-99ca8e491d2d)
- [HRESULT](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-erref/705fb797-2175-4a90-b5a3-3918024b10b8)
- [Kerberos](https://learn.microsoft.com/en-us/windows/security/threat-protection/auditing/event-4771)
- [Audit labels](https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/plan/appendix-l--events-to-monitor)

Microsoft reference material retains its original copyright and terms; see
`licenses/Microsoft-reference-notice.txt`. EventLens-authored implementation and
plain-language investigation rules are MIT licensed.
