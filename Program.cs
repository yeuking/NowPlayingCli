using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Media;
using Windows.Media.Control;

namespace NowPlayingCli
{
    class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleOutputCP(uint wCodePageID);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCP(uint wCodePageID);

        static string PrintMediaProperties(GlobalSystemMediaTransportControlsSessionMediaProperties props, string videoSymbol, string musicSymbol)
        {
            if (props.PlaybackType.HasValue)
            {
                switch (props.PlaybackType.Value)
                {
                    case MediaPlaybackType.Music:
                    case MediaPlaybackType.Unknown:
                        return FormatMusicLine(props, musicSymbol);
                    case MediaPlaybackType.Video:
                        return $"{videoSymbol} {props.Title}";
                }
            }

            // Some SMTC sources (e.g. certain AIMP integrations) omit PlaybackType but still send title/artist.
            if (!string.IsNullOrWhiteSpace(props.Title) || !string.IsNullOrWhiteSpace(props.Artist))
                return FormatMusicLine(props, musicSymbol);
            return "";
        }

        static string FormatMusicLine(GlobalSystemMediaTransportControlsSessionMediaProperties props, string musicSymbol)
        {
            var title = props.Title ?? "";
            var artist = props.Artist ?? "";
            if (string.IsNullOrWhiteSpace(artist))
                return $"{musicSymbol} {title}".TrimEnd();
            if (string.IsNullOrWhiteSpace(title))
                return $"{musicSymbol} {artist}";
            return $"{musicSymbol} {title} by {artist}";
        }

        static readonly string[] defaultPrograms = { "spotify.exe" };
        static readonly MediaPlaybackType[] defaultPlaybackTypes =
        {
            MediaPlaybackType.Music,
            MediaPlaybackType.Video,
            MediaPlaybackType.Unknown
        };

        static void PrintUsage(string program, TextWriter writer)
        {
            writer.WriteLine($"{program} [OPTIONS] [PROGRAMS...]");
            writer.WriteLine($"  Where OPTIONS may be any of the following");
            writer.WriteLine($"    -h,--help                 This text");
            writer.WriteLine($"    -t,--type <video|music|unknown>  Add playback type (repeatable). unknown helps some players (e.g. AIMP) that omit type.");
            writer.WriteLine($"    -m,--icon-music <ICON>    Use ICON as the icon for music playback");
            writer.WriteLine($"    -v,--icon-video <ICON>    Use ICON as the icon for video playback");
            writer.WriteLine($"    -d,--listen     <PORT>    Instead of printing to the console, listen \n" +
                             $"                              for TCP connections on <PORT>, printing media\n" +
                             $"                              information, then closing the connection\n");
            writer.WriteLine($"    -L,--list-sessions        Print all media session app ids, then exit (for debugging filters)\n");
            writer.WriteLine($"  And PROGRAMS is a list of substrings matched against each session's app id (case-insensitive).");
            writer.WriteLine($"  Examples: spotify.exe  aimp  (defaults to spotify.exe if none given)");
        }

        static bool PlaybackAllowed(GlobalSystemMediaTransportControlsSessionMediaProperties props, IEnumerable<MediaPlaybackType> playbackTypes)
        {
            if (props == null) return false;
            if (props.PlaybackType.HasValue)
                return playbackTypes.Contains(props.PlaybackType.Value);

            // Type not set — show if music-style metadata exists and music (or unknown) is accepted.
            var allowUntyped = playbackTypes.Contains(MediaPlaybackType.Music) || playbackTypes.Contains(MediaPlaybackType.Unknown);
            return allowUntyped
                && (!string.IsNullOrWhiteSpace(props.Title) || !string.IsNullOrWhiteSpace(props.Artist));
        }

        static bool SessionMatchesPrograms(GlobalSystemMediaTransportControlsSession session, IEnumerable<string> programs)
        {
            var id = session?.SourceAppUserModelId;
            if (string.IsNullOrEmpty(id)) return false;
            var lower = id.ToLowerInvariant();
            foreach (var p in programs)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (lower.Contains(p.ToLowerInvariant())) return true;
            }
            return false;
        }

        static int ListSessions(GlobalSystemMediaTransportControlsSessionManager sessionManager)
        {
            var sessions = sessionManager.GetSessions();
            if (sessions.Count == 0)
            {
                Console.Error.WriteLine("No media sessions — nothing is registered with Windows global media controls.");
                Console.Error.WriteLine("AIMP: install the add-on \"Windows 10 Media Control\" from the AIMP catalog, restart AIMP, then play a track.");
                return 0;
            }

            foreach (var session in sessions)
            {
                var id = session.SourceAppUserModelId ?? "";
                string extra = "";
                try
                {
                    var props = session.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
                    if (props?.PlaybackType != null && props.PlaybackType.HasValue)
                        extra = $" playbackType={props.PlaybackType.Value}";
                    else if (props != null)
                        extra = " playbackType=(unset)";
                    if (!string.IsNullOrEmpty(props?.Title))
                        extra += $" title=\"{props.Title}\"";
                }
                catch
                {
                    // ignore — session may not expose properties
                }
                Console.WriteLine($"{id}{extra}");
            }
            return 0;
        }

        static int Listen(int port, Func<string> callback)
        {
            var listener = TcpListener.Create(port);

            try
            {
                listener.Start();

                while (true)
                {
                    var handler = listener.AcceptSocket();
                    byte[] msg = Encoding.UTF8.GetBytes(callback());

                    handler.Send(msg);
                    handler.Shutdown(SocketShutdown.Both);
                    handler.Close();
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            return 0;
        }

        static int Main(string[] args)
        {
            SetConsoleOutputCP(65001);
            SetConsoleCP(65001);

            var programName = $"{Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0])}.exe";

            var videoSymbol = "🎬";
            var musicSymbol = "🎵";

            int? listenPort = null;

            var userPrograms = new List<string>();
            var userPlaybackTypes = new List<MediaPlaybackType>();
            var listSessionsOnly = false;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("-"))
                {
                    if (args[i] == "-h" || args[i] == "--help")
                    {
                        PrintUsage(programName, Console.Out);
                        return 0;
                    }
                    if (args[i] == "-L" || args[i] == "--list-sessions")
                    {
                        listSessionsOnly = true;
                        continue;
                    }
                    else if (args[i] == "-d" || args[i] == "--listen")
                    {
                        if (args.Length > i + 1)
                        {
                            if(int.TryParse(args[i+1], out var result))
                            {
                                listenPort = result;
                            }
                            else
                            {
                                Console.Error.WriteLine($"Not a valid port: {args[i+1]}");
                                PrintUsage(programName, Console.Error);
                                return 1;
                            }
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine($"Incomplete parameter: {args[i]}");
                            PrintUsage(programName, Console.Error);
                            return 1;
                        }
                    }
                    else if (args[i] == "-m" || args[i] == "--icon-music")
                    {
                        if(args.Length > i + 1)
                        {
                            musicSymbol = args[i + 1];
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine($"Incomplete parameter: {args[i]}");
                            PrintUsage(programName, Console.Error);
                            return 1;
                        }
                    }
                    else if (args[i] == "-v" || args[i] == "--icon-video")
                    {
                        if (args.Length > i + 1)
                        {
                            videoSymbol = args[i + 1];
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine($"Incomplete parameter: {args[i]}");
                            PrintUsage(programName, Console.Error);
                            return 1;
                        }
                    }
                    else if (args[i] == "-t" || args[i] == "--type")
                    {
                        if (args.Length > i + 1)
                        {
                            if (args[i + 1] == "video")
                            {
                                userPlaybackTypes.Add(MediaPlaybackType.Video);
                            }
                            else if (args[i + 1] == "music")
                            {
                                userPlaybackTypes.Add(MediaPlaybackType.Music);
                            }
                            else
                            {
                                Console.Error.WriteLine($"Unknown playback type: {args[i + 1]}");
                                PrintUsage(programName, Console.Error);
                            }
                            i++;
                        }
                        else
                        {
                            Console.Error.WriteLine($"Incomplete parameter: {args[i]}");
                            PrintUsage(programName, Console.Error);
                            return 1;
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine($"Unknown parameter: {args[i]}");
                        PrintUsage(programName, Console.Error);
                        return 1;
                    }
                }
                else
                {
                    userPrograms.Add(args[i]);
                }
            }


            IEnumerable<string> programs = userPrograms.Count > 0 ? (IEnumerable<string>)userPrograms : defaultPrograms;
            IEnumerable<MediaPlaybackType> playbackTypes = userPlaybackTypes.Count > 0 ? (IEnumerable<MediaPlaybackType>)userPlaybackTypes : defaultPlaybackTypes;

            var sessionManager = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().GetAwaiter().GetResult();
            if (listSessionsOnly)
                return ListSessions(sessionManager);

            Func<string> printer = () =>
            {
                foreach (var session in sessionManager.GetSessions())
                {
                    if (!SessionMatchesPrograms(session, programs)) continue;
                    var mediaProperties = session.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
                    if (!PlaybackAllowed(mediaProperties, playbackTypes)) continue;
                    var line = PrintMediaProperties(mediaProperties, videoSymbol, musicSymbol);
                    if (!string.IsNullOrEmpty(line)) return line;
                }
                return "";
            };

            if (listenPort.HasValue)
            {
                Console.WriteLine($"Listening for requests on port: {listenPort.Value}");
                Listen(listenPort.Value, printer);
            }
            else
            {
                Console.WriteLine(printer());
            }

            return 0;
        }
    }
}
