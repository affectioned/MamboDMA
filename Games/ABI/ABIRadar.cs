// File: Games/ABI/WebRadarServer.cs
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace MamboDMA.Games.ABI
{
    internal sealed class WebRadarServer : IDisposable
    {
        private readonly HttpListener _http = new();
        private Thread _acceptThread;
        private Thread _broadcastThread;
        private volatile bool _running;
        private readonly List<SseClient> _clients = new();
        private readonly object _clientsLock = new();

        public int Port { get; private set; } = 8088;
        public bool IsRunning => _running;
        public string Prefix { get; private set; }
        public string LastError { get; private set; } = null;

        private int _hz = 20;
        public void SetRate(int hz) => _hz = Math.Clamp(hz, 1, 60);

        public WebRadarServer(int port)
        {
            Port = port;
            // IMPORTANT: localhost avoids URLACL requirement
            Prefix = $"http://localhost:{Port}/";
            _http.Prefixes.Add(Prefix);
        }

        public void Start()
        {
            if (_running) return;

            LastError = null;

            if (!HttpListener.IsSupported)
            {
                LastError = "HttpListener is not supported on this platform.";
                throw new NotSupportedException(LastError);
            }

            try
            {
                _http.Start();
            }
            catch (HttpListenerException ex)
            {
                LastError = $"Failed to start on {Prefix}. If you switch to http://+:{Port}/ you must grant URL ACL:\n" +
                            $"  netsh http add urlacl url=http://+:{Port}/ user=Everyone";
                throw new InvalidOperationException(LastError, ex);
            }
            catch (Exception ex)
            {
                LastError = $"Start failed: {ex.GetType().Name}: {ex.Message}";
                throw;
            }

            _running = true;

            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "WebRadar.Accept" };
            _acceptThread.Start();

            _broadcastThread = new Thread(BroadcastLoop) { IsBackground = true, Name = "WebRadar.Broadcast" };
            _broadcastThread.Start();
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;

            try { _http.Stop(); } catch { }

            try { _acceptThread?.Join(250); } catch { }
            try { _broadcastThread?.Join(250); } catch { }

            lock (_clientsLock)
            {
                foreach (var c in _clients) c.Dispose();
                _clients.Clear();
            }
        }

        public void Dispose() => Stop();

        private void AcceptLoop()
        {
            while (_running)
            {
                HttpListenerContext ctx = null;
                try { ctx = _http.GetContext(); }
                catch when (!_running) { break; }
                catch { continue; }

                if (ctx?.Request?.Url == null) { SafeClose(ctx); continue; }
                var path = ctx.Request.Url.AbsolutePath ?? "/";

                if (path.Equals("/stream", StringComparison.OrdinalIgnoreCase)) { HandleSse(ctx); continue; }
                if (path.Equals("/api/frame", StringComparison.OrdinalIgnoreCase)) { HandleApiFrame(ctx); continue; }
                if (path.Equals("/ping", StringComparison.OrdinalIgnoreCase)) { HandlePing(ctx); continue; }

                HandleStatic(ctx);
            }
        }

        private void HandlePing(HttpListenerContext ctx)
        {
            try
            {
                var msg = Encoding.UTF8.GetBytes("pong");
                ctx.Response.ContentType = "text/plain";
                ctx.Response.ContentLength64 = msg.Length;
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                ctx.Response.OutputStream.Write(msg, 0, msg.Length);
            }
            catch { }
            finally { SafeClose(ctx); }
        }

        private void HandleSse(HttpListenerContext ctx)
        {
            try
            {
                var resp = ctx.Response;
                resp.StatusCode = 200;
                resp.SendChunked = true;
                resp.ContentType = "text/event-stream";
                resp.Headers.Add("Cache-Control", "no-cache");
                resp.Headers.Add("Access-Control-Allow-Origin", "*");
                resp.Headers.Add("Connection", "keep-alive");
                resp.Headers.Add("X-Accel-Buffering", "no"); // avoid proxy buffering
                var client = new SseClient(resp);
                lock (_clientsLock) _clients.Add(client);

                client.PumpUntilClosed();

                lock (_clientsLock) _clients.Remove(client);
                client.Dispose();
            }
            catch { SafeClose(ctx); }
        }

        private void HandleApiFrame(HttpListenerContext ctx)
        {
            string json = BuildFrameJson();
            try
            {
                byte[] buf = Encoding.UTF8.GetBytes(json);
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = buf.Length;
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            }
            catch { }
            finally { SafeClose(ctx); }
        }

        private void HandleStatic(HttpListenerContext ctx)
        {
            try
            {
                var req = ctx.Request;
                string rel = req.Url.AbsolutePath;
                if (string.IsNullOrWhiteSpace(rel) || rel == "/") rel = "/index.html";

                rel = rel.Replace('\\', '/').TrimStart('/');

                string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "WebRadar");
                string full = Path.GetFullPath(Path.Combine(root, rel));

                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Response.StatusCode = 403; SafeClose(ctx); return;
                }

                if (!File.Exists(full))
                {
                    ctx.Response.StatusCode = 404; SafeClose(ctx); return;
                }

                string mime = GuessMime(Path.GetExtension(full));
                byte[] data = File.ReadAllBytes(full);

                ctx.Response.ContentType = mime;
                ctx.Response.ContentLength64 = data.Length;
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                ctx.Response.OutputStream.Write(data, 0, data.Length);
            }
            catch { }
            finally { SafeClose(ctx); }
        }

        private static string GuessMime(string ext)
        {
            ext = (ext ?? "").ToLowerInvariant();
            return ext switch
            {
                ".html" => "text/html; charset=utf-8",
                ".htm"  => "text/html; charset=utf-8",
                ".js"   => "application/javascript; charset=utf-8",
                ".css"  => "text/css; charset=utf-8",
                ".png"  => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif"  => "image/gif",
                ".svg"  => "image/svg+xml",
                _       => "application/octet-stream",
            };
        }

        private void BroadcastLoop()
        {
            var sw = new System.Diagnostics.Stopwatch();
            while (_running)
            {
                sw.Restart();
                string payload = BuildFrameJson();

                lock (_clientsLock)
                {
                    for (int i = _clients.Count - 1; i >= 0; i--)
                    {
                        var c = _clients[i];
                        if (!c.IsAlive) { c.Dispose(); _clients.RemoveAt(i); continue; }
                        try { c.SendEvent(payload); }
                        catch { c.Dispose(); _clients.RemoveAt(i); }
                    }
                }

                int targetMs = Math.Max(5, 1000 / Math.Clamp(_hz, 1, 60));
                int sleep = targetMs - (int)sw.ElapsedMilliseconds;
                if (sleep > 0) Thread.Sleep(sleep);
            }
        }

        private string BuildFrameJson()
        {
            if (!Players.TryGetFrame(out var fr) || fr.Positions == null)
                return "{\"ok\":false}";

            float yawDeg = fr.Cam.Rotation.Yaw;

            List<Players.ABIPlayer> actors;
            lock (Players.Sync)
                actors = Players.ActorList.Count > 0 ? new List<Players.ABIPlayer>(Players.ActorList) : new();

            var sb = new StringBuilder(1 << 14);
            sb.Append("{\"ok\":true");
            sb.Append(",\"self\":{");
            sb.AppendFormat("\"x\":{0:0.###},\"y\":{1:0.###},\"yaw\":{2:0.###}", fr.Local.X, fr.Local.Y, yawDeg);
            sb.Append("}");

            sb.Append(",\"actors\":[");
            bool first = true;

            var posMap = new Dictionary<ulong, Players.ActorPos>(fr.Positions.Count);
            for (int i = 0; i < fr.Positions.Count; i++) posMap[fr.Positions[i].Pawn] = fr.Positions[i];

            for (int i = 0; i < actors.Count; i++)
            {
                var a = actors[i];
                if (!posMap.TryGetValue(a.Pawn, out var ap)) continue;

                if (!first) sb.Append(',');
                first = false;
                sb.Append('{');
                sb.AppendFormat("\"x\":{0:0.###},\"y\":{1:0.###},\"dead\":{2},\"bot\":{3}",
                    ap.Position.X, ap.Position.Y,
                    ap.IsDead ? "true" : "false",
                    a.IsBot ? "true" : "false");
                sb.Append('}');
            }
            sb.Append(']');
            sb.Append('}');

            return sb.ToString();
        }

        private static void SafeClose(HttpListenerContext ctx)
        {
            try { ctx.Response.OutputStream.Flush(); } catch { }
            try { ctx.Response.OutputStream.Dispose(); } catch { }
            try { ctx.Response.Close(); } catch { }
        }

        private sealed class SseClient : IDisposable
        {
            private readonly HttpListenerResponse _resp;
            private readonly Stream _stream;
            private volatile bool _alive = true;

            public SseClient(HttpListenerResponse resp)
            {
                _resp = resp;
                _stream = resp.OutputStream;
                SendRaw(":ok\n\n");
            }

            public bool IsAlive => _alive;

            public void PumpUntilClosed()
            {
                new Thread(() =>
                {
                    try
                    {
                        while (_alive)
                        {
                            Thread.Sleep(15000);
                            SendComment("ping");
                        }
                    }
                    catch { _alive = false; }
                }) { IsBackground = true }.Start();
            }

            public void SendEvent(string json)
            {
                if (!_alive) return;
                SendRaw("event: frame\n");
                SendRaw("data: "); SendRaw(json); SendRaw("\n\n");
            }

            public void SendComment(string cmt)
            {
                if (!_alive) return;
                SendRaw($":{cmt}\n\n");
            }

            private void SendRaw(string s)
            {
                try
                {
                    var buf = Encoding.UTF8.GetBytes(s);
                    _stream.Write(buf, 0, buf.Length);
                    _stream.Flush();
                }
                catch { _alive = false; }
            }

            public void Dispose()
            {
                _alive = false;
                try { _stream.Dispose(); } catch { }
                try { _resp.Close(); } catch { }
            }
        }
    }

    internal static class WebRadarUI
    {
        private static WebRadarServer _srv;
        private static int _port = 8088;
        private static int _rate = 20;
        private static bool _autoOpen = true;
        private static string _lastStatus = ""; // persist message across frames

        public static void DrawPanel()
        {
            ImGui.Text("Web Radar");
            ImGui.PushItemWidth(100);
            ImGui.InputInt("Port", ref _port);
            ImGui.PopItemWidth();
            ImGui.SliderInt("Stream FPS", ref _rate, 1, 60);
            ImGui.Checkbox("Open browser on start", ref _autoOpen);

            bool running = _srv != null && _srv.IsRunning;

            if (!running)
            {
                if (ImGui.Button("Start WebServer"))
                {
                    try
                    {
                        _srv?.Dispose();
                        _srv = new WebRadarServer(_port);
                        _srv.SetRate(_rate);
                        _srv.Start();
                        _lastStatus = $"Running at {_srv.Prefix}";
                        if (_autoOpen)
                        {
                            var url = $"http://localhost:{_port}/";
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = url,
                                UseShellExecute = true
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _lastStatus = $"Error: {ex.Message}";
                    }
                }
            }
            else
            {
                if (ImGui.Button("Stop WebServer"))
                {
                    try { _srv?.Stop(); _lastStatus = "Stopped."; } catch { }
                }
                ImGui.SameLine();
                if (ImGui.Button("Open in Browser"))
                {
                    var url = $"http://localhost:{_port}/";
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex) { _lastStatus = $"Open error: {ex.Message}"; }
                }
                ImGui.SameLine();
                if (ImGui.Button("Open Assets Folder"))
                {
                    try
                    {
                        var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "WebRadar");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = root,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex) { _lastStatus = $"Open folder error: {ex.Message}"; }
                }
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.2f, 0.9f, 0.2f, 1), $"http://localhost:{_port}/");
            }

            if (!string.IsNullOrEmpty(_lastStatus))
            {
                var col = _lastStatus.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
                    ? new Vector4(1, .5f, .5f, 1)
                    : new Vector4(.6f, .9f, .6f, 1);
                ImGui.TextColored(col, _lastStatus);
            }

            // quick test buttons
            if (ImGui.Button("Test /ping")) TryOpen($"http://localhost:{_port}/ping");
            ImGui.SameLine();
            if (ImGui.Button("Test /api/frame")) TryOpen($"http://localhost:{_port}/api/frame");
        }

        public static void StopIfRunning()
        {
            try { _srv?.Stop(); } catch { }
        }

        private static void TryOpen(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { _lastStatus = $"Open error: {ex.Message}"; }
        }
    }
}
