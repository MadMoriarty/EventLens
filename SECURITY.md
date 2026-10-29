# Security

EventLens is a defensive, read-only event investigation utility. Report issues
privately to the repository owner using the hosting platform's private security
reporting mechanism when available. Do not include private logs in public issues.

XML parsing prohibits DTD/external entity resolution and bounds document size.
Event text is displayed as text, not executable markup. The app never follows
commands embedded in events. CSV cells beginning with spreadsheet formula syntax
are prefixed; JSON retains raw evidence. Logs/settings/exports are not encrypted.

Remote operations use the current Windows identity and supported Event Log APIs.
No passwords are requested/stored, and no Windows permissions or firewall settings
are changed. Subscription disconnects/overflows are reported and can create gaps.

The self-contained runtime and libraries need periodic dependency updates. The
binary is unsigned; there is no automatic update/downloader or signature claim.

Version 1.3 creates temporary SQLite investigation caches in the current user's
EventLens-QueryCache temporary directory. They contain original event evidence,
use normal user-directory access controls, are not additionally encrypted, and are
deleted on normal query disposal. Forced exit/crash may leave private cache files.
No cache is included in source/distribution archives. Review reports before sharing.
