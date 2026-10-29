# Troubleshooting and reference sources

EventLens 1.2 integrates all three requested sources:

| Source | Integration |
| --- | --- |
| Microsoft troubleshooting / audit documentation | Selected event-specific diagnostic and schema links, plus the troubleshooting library for unmapped events |
| Ultimate Windows Security | 389 indexed modern Windows audit event-page URLs, matched by provider and event ID |
| EventSentry Security Log Defined | 382 indexed modern Windows audit event-page URLs, matching code lookup links, and its reference library |

The indexes were checked on 2026-10-22. Counts are links discovered in the
publishers' event lists, not counts of copied articles, tested remedies or offline
guides. A provider with a coincidentally equal event number does not get another
provider's article. Unmapped providers receive clearly labeled library links.

## Use

Select an event, then open **Help** in the right pane, or click **Troubleshooting
& references** under Explain. Help separates recorded facts, possible causes,
next checks, conditional resolutions, related evidence and applicability.
Reference buttons open in the default browser only when clicked. The lower-left
**References & troubleshooting** button opens the source directory, integration
coverage and the original offline code-catalog coverage.

Offline EventLens-authored guides cover authentication failures and code-specific
checks, lockouts, successful logons, account/password/group changes, process
creation, audit-log clearing, SCM failures/installations, application crashes,
unexpected shutdowns and Task Scheduler outcomes. Guide selection uses provider,
event ID, recorded outcome and relevant status/substatus. There is no universal
repair based on a number alone. Unmapped events explicitly say no specific guide
is available. Windows/product versions must match the linked article.

The offline guidance and existing numeric decoder remain available without a
network connection. Third-party article text and entire website databases are
not embedded or scraped into reports. External publishers retain their rights.
Our independent guidance cites the references and is not vendor endorsement.

## Data handling and reports

No background requests, log uploads or remote AI calls are introduced. Reference
URLs contain only fixed documentation paths and public event/code identifiers.
They do not include recorded account names, computer names, IP addresses, original
messages or XML. The browser and reference website handle normal web navigation.

**Copy troubleshooting and references** copies the displayed guide and URLs.
Existing JSON/CSV exports now include troubleshooting and reference attribution,
in addition to original event data and decoded codes. They still cover loaded or
explicitly selected records only. Exported reports contain event data and should
be handled according to your organization's procedures.

## Maintenance

`python tools/update-knowledge-links.py` rebuilds the URL-only index from the two
public indexes. Original HTML is held only in ignored `.research/knowledge`.
The embedded index retains each source URL and HTML SHA-256. Review event scope,
guide applicability and the reference date when updating it. Routine builds and
normal app launch do not download references.

Source sites:

- https://learn.microsoft.com/en-us/troubleshoot/
- https://www.ultimatewindowssecurity.com/securitylog/encyclopedia/default.aspx
- https://system32.eventsentry.com/

Selected Microsoft troubleshooting articles:

- https://learn.microsoft.com/en-us/troubleshoot/windows-server/windows-security/account-lockout-and-management-tool
- https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/event-id-41-restart
- https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/troubleshoot-application-service-crashing-behavior
- https://learn.microsoft.com/en-us/troubleshoot/windows-server/system-management-components/troubleshoot-scheduled-tasks-not-running
