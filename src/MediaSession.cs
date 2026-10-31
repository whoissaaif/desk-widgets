using System;
using System.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DeskWidgets
{
    // Wraps the Windows media session (the one behind the volume flyout's media controls): whatever
    // is playing in Spotify, a browser, Media Player... Windows pushes changes to us as events, so
    // nothing is polled. All calls block, so use this from a background thread.
    public sealed class MediaSession : IDisposable
    {
        public sealed class Snapshot
        {
            public string Title, Artist, App;
            public bool Playing, CanPrevious, CanNext;
            public TimeSpan Position, Duration;
            public DateTime PositionAtUtc;
            public byte[] Art;
        }

        GlobalSystemMediaTransportControlsSessionManager manager;
        GlobalSystemMediaTransportControlsSession session;
        readonly object gate = new object();

        // Raised on a worker thread whenever the track, play state or position changes.
        public event Action Changed;

        TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs> onSession;
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> onProps;
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> onPlayback;
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> onTimeline;

        public static MediaSession Open()
        {
            var m = new MediaSession();
            m.manager = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
            m.onSession = (s, e) => m.Attach();
            m.onProps = (s, e) => m.Raise();
            m.onPlayback = (s, e) => m.Raise();
            m.onTimeline = (s, e) => m.Raise();
            m.manager.CurrentSessionChanged += m.onSession;
            m.Attach();
            return m;
        }

        void Attach()
        {
            lock (gate)
            {
                Detach();
                session = manager.GetCurrentSession();
                if (session != null)
                {
                    session.MediaPropertiesChanged += onProps;
                    session.PlaybackInfoChanged += onPlayback;
                    session.TimelinePropertiesChanged += onTimeline;
                }
            }
            Raise();
        }

        void Detach()
        {
            if (session == null) return;
            session.MediaPropertiesChanged -= onProps;
            session.PlaybackInfoChanged -= onPlayback;
            session.TimelinePropertiesChanged -= onTimeline;
            session = null;
        }

        void Raise()
        {
            var h = Changed;
            if (h != null) h();
        }

        // Null when nothing is playing.
        public Snapshot Read()
        {
            GlobalSystemMediaTransportControlsSession s;
            lock (gate) s = session;
            if (s == null) return null;
            var props = Wait(s.TryGetMediaPropertiesAsync());
            var playback = s.GetPlaybackInfo();
            var timeline = s.GetTimelineProperties();
            var snap = new Snapshot
            {
                Title = props.Title ?? "", Artist = props.Artist ?? props.AlbumArtist ?? "", App = AppName(s.SourceAppUserModelId),
                Playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                CanPrevious = playback.Controls.IsPreviousEnabled, CanNext = playback.Controls.IsNextEnabled,
                Position = timeline.Position, Duration = timeline.EndTime - timeline.StartTime,
                PositionAtUtc = timeline.LastUpdatedTime.UtcDateTime
            };
            if (props.Thumbnail != null)
            {
                try
                {
                    using (var ras = Wait(props.Thumbnail.OpenReadAsync()))
                    using (var reader = new DataReader(ras.GetInputStreamAt(0)))
                    {
                        uint size = (uint)Math.Min(ras.Size, 8 * 1024 * 1024);
                        uint got = Wait(reader.LoadAsync(size));
                        snap.Art = new byte[got];
                        reader.ReadBytes(snap.Art);
                    }
                }
                catch (Exception) { } // no art is fine
            }
            return snap;
        }

        public void PlayPause() { Send(s => s.TryTogglePlayPauseAsync()); }
        public void Next() { Send(s => s.TrySkipNextAsync()); }
        public void Previous() { Send(s => s.TrySkipPreviousAsync()); }

        void Send(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command)
        {
            GlobalSystemMediaTransportControlsSession s;
            lock (gate) s = session;
            if (s != null) { var ignored = command(s); } // fire and forget; the session reports the result as an event
        }

        // Blocks until a WinRT async operation finishes. (The usual AsTask() helper needs the
        // Windows SDK's union metadata to compile; this needs nothing beyond what Windows ships.)
        static T Wait<T>(IAsyncOperation<T> op)
        {
            using (var done = new ManualResetEvent(false))
            {
                op.Completed = (o, status) => done.Set(); // runs immediately if already finished
                if (!done.WaitOne(15000)) { op.Cancel(); throw new TimeoutException("Windows media session did not answer"); }
                if (op.Status == AsyncStatus.Error) throw new InvalidOperationException("Windows media session error " + op.ErrorCode.HResult);
                return op.GetResults();
            }
        }

        // "Spotify.exe" -> "Spotify", "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" -> "Media Player".
        static string AppName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string name = id.Split('!')[0].Split('_')[0];
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            switch (name.ToLowerInvariant())
            {
                case "microsoft.zunemusic": return "Media Player";
                case "microsoft.zunevideo": return "Movies & TV";
                case "msedge": return "Edge";
                case "chrome": return "Chrome";
                case "firefox": return "Firefox";
                case "spotify": case "spotifyab.spotifymusic": return "Spotify";
                default: return name.Length > 0 ? char.ToUpper(name[0]) + name.Substring(1) : name;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                Detach();
                if (manager != null) manager.CurrentSessionChanged -= onSession;
                manager = null;
            }
        }
    }
}
