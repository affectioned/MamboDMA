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
        // NOTE: instance-scoped (not static) to avoid reuse-after-dispose across restarts
        private HttpListener _http;
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
        private readonly bool _enableUpnp;
        private readonly bool _bindAll; // listen on all interfaces if true
        private UpnpMapper _upnp;

        // Preferred: UPnP external endpoint; Fallback: external IP service(s)
        public string ExternalUrl { get; private set; } // e.g. http://1.2.3.4:8088/
        public string ExternalIp  { get; private set; } // just the IP part
        public void TriggerExternalIpRefresh() { _ = RefreshExternalIpFallbackAsync(); }

        public WebRadarServer(int port, bool enableUpnp = false, bool bindAll = false)
        {
            Port = port;
            _enableUpnp = enableUpnp;
            _bindAll = bindAll;

            // IMPORTANT:
            // - localhost avoids URLACL but is not reachable from LAN/Internet.
            // - http://+:{port}/ listens on all interfaces (requires URLACL).
            Prefix = _bindAll ? $"http://+:{Port}/" : $"http://localhost:{Port}/";
        }

        public void Start()
        {
            if (_running) return;
            LastError = null;

            if (!HttpListener.IsSupported)
                throw new NotSupportedException(LastError = "HttpListener is not supported on this platform.");

            try
            {
                // fresh listener every time we start
                _http = new HttpListener();
                _http.Prefixes.Add(Prefix);
                _http.Start();
            }
            catch (HttpListenerException hex)
            {
                // Most common cause is missing URL ACL (Access is denied.)
                if (Prefix.StartsWith("http://+:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!UpnpMapper.EnsureUrlAcl(Port, out var aclErr))
                        throw new InvalidOperationException(
                            LastError = $"Failed to create URL ACL automatically: {aclErr}", hex);

                    // Optional: open firewall
                    UpnpMapper.EnsureFirewallRule(Port, "MamboDMA WebRadar", out _);

                    // Try again after ACL is created (with a brand-new instance)
                    _http = new HttpListener();
                    _http.Prefixes.Add(Prefix);
                    _http.Start();
                }
                else
                {
                    throw new InvalidOperationException(
                        LastError = $"Failed to start on {Prefix}: {hex.Message}", hex);
                }
            }
            catch (Exception ex)
            {
                LastError = $"Start failed: {ex.GetType().Name}: {ex.Message}";
                // ensure we don't keep a half-initialized listener around
                try { _http?.Close(); } catch { }
                _http = null;
                throw;
            }

            // Optional UPnP map (best-effort)
            if (_enableUpnp)
            {
                try
                {
                    _upnp?.Dispose();
                    _upnp = new UpnpMapper();
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        if (await _upnp.TryMapAsync(Port, Port, "MamboDMA WebRadar", cts.Token))
                        {
                            ExternalIp = _upnp.ExternalEndPoint?.Address?.ToString();
                            if (!string.IsNullOrEmpty(ExternalIp))
                                ExternalUrl = $"http://{ExternalIp}:{Port}/";
                        }
                        else
                        {
                            LastError = _upnp.LastError;
                        }
                    });
                }
                catch (Exception ex) { LastError = ex.Message; }
            }

            _running = true;

            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "WebRadar.Accept" };
            _acceptThread.Start();

            _broadcastThread = new Thread(BroadcastLoop) { IsBackground = true, Name = "WebRadar.Broadcast" };
            _broadcastThread.Start();
        }

        public void Stop()
        {
            if (!_running && _http == null) return;

            _running = false;

            // Close all clients first so their streams don't keep the listener alive
            lock (_clientsLock)
            {
                foreach (var c in _clients) { try { c.Dispose(); } catch { } }
                _clients.Clear();
            }

            try { _http?.Stop(); } catch { }
            try { _http?.Close(); } catch { }   // ensures ObjectDisposed for any stray calls
            try { _http = null; } catch { }

            try { _acceptThread?.Join(500); } catch { }
            try { _broadcastThread?.Join(500); } catch { }
            try { _upnp?.Dispose(); _upnp = null; ExternalUrl = null; } catch { }
        }

        public void Dispose() => Stop();

        private void AcceptLoop()
        {
            var listener = _http; // capture to avoid races if _http is nulled in Stop()

            while (_running && listener != null)
            {
                HttpListenerContext ctx = null;
                try
                {
                    ctx = listener.GetContext();
                }
                catch (ObjectDisposedException)
                {
                    break; // listener fully disposed
                }
                catch (HttpListenerException hex)
                {
                    // 995/Operation canceled or any error while stopping -> exit
                    if (!_running || listener == null || !listener.IsListening) break;
                    // transient error; continue
                    continue;
                }
                catch
                {
                    if (!_running) break;
                    continue;
                }

                if (ctx?.Request?.Url == null) { SafeClose(ctx); continue; }
                var path = ctx.Request.Url.AbsolutePath ?? "/";

                if (path.Equals("/stream", StringComparison.OrdinalIgnoreCase)) { HandleSse(ctx); continue; }
                if (path.Equals("/api/frame", StringComparison.OrdinalIgnoreCase)) { HandleApiFrame(ctx); continue; }
                if (path.Equals("/ping", StringComparison.OrdinalIgnoreCase)) { HandlePing(ctx); continue; }
                if (path.Equals("/status", StringComparison.OrdinalIgnoreCase)) { HandleStatus(ctx); continue; }
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
            ulong sessionId = Players.PersistentLevel;  // new raid ¡ú new id
            float camFov = fr.Cam.Fov;

            List<Players.ABIPlayer> actors;
            lock (Players.Sync)
                actors = Players.ActorList.Count > 0 ? new List<Players.ABIPlayer>(Players.ActorList) : new();

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder(1 << 14);

            sb.Append("{\"ok\":true");
            sb.Append(",\"session\":"); sb.Append(sessionId.ToString());
            sb.Append(",\"fov\":"); sb.Append(camFov.ToString("0.###", inv));

            // self (now with Z)
            sb.Append(",\"self\":{");
            sb.AppendFormat(inv, "\"x\":{0:0.###},\"y\":{1:0.###},\"z\":{2:0.###},\"yaw\":{3:0.###}",
                fr.Local.X, fr.Local.Y, fr.Local.Z, yawDeg);
            sb.Append('}');

            // quick lookup: Pawn -> ActorPos
            var posMap = new Dictionary<ulong, Players.ActorPos>(fr.Positions.Count);
            for (int i = 0; i < fr.Positions.Count; i++) posMap[fr.Positions[i].Pawn] = fr.Positions[i];

            // actors (add z and pawn as hex string)
            sb.Append(",\"actors\":[");
            bool first = true;
            for (int i = 0; i < actors.Count; i++)
            {
                var a = actors[i];
                if (!posMap.TryGetValue(a.Pawn, out var ap)) continue;

                if (!first) sb.Append(',');
                first = false;

                sb.Append('{');
                sb.AppendFormat(inv, "\"x\":{0:0.###},\"y\":{1:0.###},\"z\":{2:0.###}", ap.Position.X, ap.Position.Y, ap.Position.Z);
                sb.Append(",\"dead\":"); sb.Append(ap.IsDead ? "true" : "false");
                sb.Append(",\"bot\":"); sb.Append(a.IsBot ? "true" : "false");

                // IMPORTANT: pawn as string to avoid JS precision loss
                sb.Append(",\"pawn\":\"0x"); sb.Append(a.Pawn.ToString("X")); sb.Append('\"');

                sb.Append('}');
            }
            sb.Append(']');

            // optional public IP/export
            if (!string.IsNullOrEmpty(ExternalIp))
            {
                sb.Append(",\"publicIp\":\""); sb.Append(ExternalIp); sb.Append('\"');
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static void SafeClose(HttpListenerContext ctx)
        {
            try { ctx.Response.OutputStream.Flush(); } catch { }
            try { ctx.Response.OutputStream.Dispose(); } catch { }
            try { ctx.Response.Close(); } catch { }
        }

        private void HandleStatus(HttpListenerContext ctx)
        {
            try
            {
                // Get local interface IPs and stringify them to avoid Join() ambiguity
                var addrs = System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName());
                var ipStrings = Array.ConvertAll(addrs ?? Array.Empty<System.Net.IPAddress>(), a => a.ToString());
                var ipsJson = string.Join(",", Array.ConvertAll(ipStrings, s => $"\"{s}\""));

                string publicUrl = GetPublicUrl() ?? (string.IsNullOrEmpty(ExternalIp) ? null : $"http://{ExternalIp}:{Port}/");

                string json = "{"
                    + $"\"listening\":true"
                    + $",\"prefix\":\"{Prefix}\""
                    + $",\"externalUrl\":{(string.IsNullOrEmpty(ExternalUrl) ? "null" : $"\"{ExternalUrl}\"")}"
                    + $",\"publicIp\":{(string.IsNullOrEmpty(ExternalIp) ? "null" : $"\"{ExternalIp}\"")}"
                    + $",\"publicUrl\":{(string.IsNullOrEmpty(publicUrl) ? "null" : $"\"{publicUrl}\"")}"
                    + $",\"upnpLastError\":{(string.IsNullOrEmpty(LastError) ? "null" : $"\"{LastError}\"")}"
                    + $",\"lanIPs\":[{ipsJson}]"
                    + $",\"time\":\"{DateTime.UtcNow:o}\""
                    + "}";

                var buf = Encoding.UTF8.GetBytes(json);
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = buf.Length;
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            }
            catch { }
            finally { SafeClose(ctx); }
        }

        // Build best public URL we know about
        public string GetPublicUrl()
        {
            if (!string.IsNullOrEmpty(ExternalUrl)) return ExternalUrl.TrimEnd('/');
            if (!string.IsNullOrEmpty(ExternalIp)) return $"http://{ExternalIp}:{Port}/";
            return null;
        }

        // Fallback external-IP discovery (ipify, icanhazip, ifconfig.me)
        private async System.Threading.Tasks.Task RefreshExternalIpFallbackAsync()
        {
            try
            {
                // If UPnP already gave us a valid external address, keep it.
                if (!string.IsNullOrEmpty(ExternalIp))
                    return;

                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(5);

                var services = new[]
                {
                    "https://api.ipify.org",
                    "https://icanhazip.com",
                    "https://ifconfig.me/ip"
                };

                foreach (var s in services)
                {
                    try
                    {
                        var txt = (await http.GetStringAsync(s)).Trim();
                        if (IPAddress.TryParse(txt, out _))
                        {
                            ExternalIp = txt;
                            if (string.IsNullOrEmpty(ExternalUrl))
                                ExternalUrl = $"http://{ExternalIp}:{Port}/";
                            return;
                        }
                    }
                    catch { /* try next */ }
                }
            }
            catch { /* ignore */ }
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
        private static bool _enableUpnp = false;
        private static bool _bindAll = false; // listen on 0.0.0.0 if true

        public static void DrawPanel()
        {
            ImGui.Text("Web Radar");
            ImGui.PushItemWidth(100);
            ImGui.InputInt("Port", ref _port);
            ImGui.PopItemWidth();
            ImGui.SliderInt("Stream FPS", ref _rate, 1, 60);
            ImGui.Checkbox("Open browser on start", ref _autoOpen);
            ImGui.Checkbox("UPnP/NAT-PMP port forward", ref _enableUpnp);
            ImGui.SameLine();
            ImGui.Checkbox("LAN/Internet access (bind 0.0.0.0)", ref _bindAll);
            if (_enableUpnp && !_bindAll)
                ImGui.TextColored(new Vector4(1f, .8f, .2f, 1f), "UPnP is on, but server will bind to localhost only ¡ª external access will still fail.");
            bool running = _srv != null && _srv.IsRunning;

            if (!running)
            {
                if (ImGui.Button("Start WebServer"))
                {
                    try
                    {
                        _srv?.Dispose();
                        _srv = new WebRadarServer(_port, enableUpnp: _enableUpnp, bindAll: _bindAll);
                        _srv.SetRate(_rate);
                        _srv.Start();
                        _lastStatus = $"Running at {_srv.Prefix}";
                        if (_autoOpen)
                        {
                            var url = $"http://localhost:{_port}/";
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
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

                // Local quick URL (always available)
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.2f, 0.9f, 0.2f, 1), $"http://localhost:{_port}/");

                // ©¤©¤ Public URL (click to copy) ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
                if (_srv != null)
                {
                    var pubUrl = _srv.GetPublicUrl();
                    ImGui.Separator();
                    ImGui.Text("Public URL:");
                    ImGui.SameLine();
                    if (!string.IsNullOrEmpty(pubUrl))
                    {
                        RenderUrlRow(pubUrl.TrimEnd('/')); // clickable + copy
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Refresh"))
                        {
                            _lastStatus = "Refreshing public IP¡­";
                            _srv.TriggerExternalIpRefresh();  // no reflection, no await
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(_srv.LastError))
                            ImGui.TextColored(new Vector4(1f, 0.6f, 0.6f, 1f), $"UPnP/Probe: {_srv.LastError}");
                        else
                            ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.6f, 1f), "Determining public IP¡­ (will show here if reachable)");
                    }
                }
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

        // Renders a clickable URL with a small "Copy" button; clicking either copies to clipboard.
        private static void RenderUrlRow(string url)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 0.8f, 1f, 1f));
            bool clicked = ImGui.Selectable(url, false);
            ImGui.PopStyleColor();

            ImGui.SameLine();
            if (ImGui.SmallButton("Copy"))
                clicked = true;

            if (clicked)
            {
                ImGui.SetClipboardText(url);
                _lastStatus = $"Copied: {url}";
            }
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

    internal static class FirewallHelper
    {
        // Best-effort: allow inbound TCP for the chosen port
        public static void TryAddInboundRule(int port, string name = "MamboDMA WebRadar")
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall add rule name=\"{name} {port}\" dir=in action=allow protocol=TCP localport={port}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(2000);
            }
            catch { /* ignore */ }
        }
    }
}
