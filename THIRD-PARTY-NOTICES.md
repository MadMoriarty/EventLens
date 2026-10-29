# Third-party acknowledgments

EventLens source is MIT licensed. NuGet lockfiles record exact dependencies.

- .NET 10 and System.Diagnostics.EventLog 10.0.13: Microsoft and contributors,
  MIT and component-specific licenses. Runtime notices accompany the release.
- Avalonia 11.3.22 and Avalonia.Controls.DataGrid 11.3.13: AvaloniaUI contributors,
  MIT. https://github.com/AvaloniaUI/Avalonia and
  https://github.com/AvaloniaUI/Avalonia.Controls.DataGrid
- SkiaSharp/Skia: Microsoft, Google and contributors, MIT/BSD and bundled notices.
- HarfBuzzSharp/HarfBuzz: MIT and accompanying third-party licenses.
- ANGLE/native graphics dependencies retain their original component notices.
- Microsoft.Data.Sqlite 10.0.13 and SQLitePCLRaw: MIT. Bundled SQLite native
  components retain their package notices (SQLite itself is public domain).
  https://github.com/dotnet/efcore and https://github.com/ericsink/SQLitePCL.raw
- xUnit and Microsoft.NET.Test.Sdk are build/test dependencies, not runtime code.

Microsoft EventLogExpert (MIT) was reviewed as an architecture reference at
commit 07a5a286b62a87e0181d18731f6dab0f52870610. Its implementation is not copied
into this app. Its MIT license is included as an acknowledgment.

The portable release keeps collected NuGet/runtime license and notice files in
licenses. The embedded Microsoft code reference retains its source attribution and
terms in licenses/Microsoft-reference-notice.txt. No Microsoft logos or original
Event Viewer UI resources are distributed. Windows provider messages are resolved at runtime by
the operating system; foreign EVTX files may lack those message resources.

Ultimate Windows Security / Randy Franklin Smith / Monterey Technology Group and
EventSentry / NETIKUS.NET retain all rights to their respective online articles,
brands and databases. EventLens distributes only public reference URLs, numeric
event identifiers and original investigation guidance, not copies of their article
text or databases. No endorsement or affiliation is implied. See
https://www.ultimatewindowssecurity.com/securitylog/encyclopedia/default.aspx and
https://system32.eventsentry.com/ . Microsoft troubleshooting articles are likewise
linked for additional reading; the offline investigation wording is EventLens-authored.
