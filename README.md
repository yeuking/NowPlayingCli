# NowPlayingCli
This is fork version of https://github.com/FredrikAleksander/NowPlayingCli

Get Now Playing information in the console on Windows.

I wanted a solution for getting AIMP Now playing information share with AI Agent

Here is what was run and what changed.

What I ran
On this machine, NowPlayingCli --list-sessions reported no media sessions (nothing is feeding Windows global media controls here), so there is no AIMP/Spotify line to show in this environment. After the latest change, that case prints a clear hint on stderr (AIMP needs the Windows 10 Media Control add-on).

dotnet "NowPlayingCli.dll" --list-sessions
dotnet "NowPlayingCli.dll" aimp

Fixes applied in code
MediaPlaybackType.Unknown is included in the default allowed types and handled like music in formatting (many Win32 integrations use Unknown).
Missing PlaybackType — if the type is unset but title or artist is present, it is treated like music when music/unknown is allowed (covers AIMP-style metadata).
-t unknown — you can add it explicitly if you use a custom -t list and still need unknown-typed sessions.
--list-sessions — if the list is empty, you get the AIMP add-on reminder on stderr.

What you should do on your PC
Install Windows 10 Media Control in AIMP (4.50+), restart AIMP, start playback.
https://aimp.ru/?F2=65&author=293&do=catalog&id=2&os=windows&sort=1

Run --list-sessions and confirm a line contains aimp (or similar).
Run with a filter, for example:

```
NowPlayingCli.exe -t music aimp
```
If a session appears but the line is still empty, try:
NowPlayingCli -t music -t unknown aimp
If anything still fails on your machine, paste the full output of --list-sessions (one run with AIMP playing).





Requires dotnet core SDK 3.1 or higher to build

