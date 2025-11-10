// File: Games/ABI/UpnpMapper.cs
using System;
using System.Diagnostics;
using System.Net;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Open.Nat;

namespace MamboDMA.Games.ABI
{
    /// <summary>
    /// UPnP / NAT-PMP mapper with auto-renew. Call TryMapAsync(), then UnmapAsync()/Dispose().
    /// </summary>
    internal sealed class UpnpMapper : IDisposable
    {
        private NatDevice _device;
        private Mapping _mapping;
        private CancellationTokenSource _renewCts;

        public IPEndPoint ExternalEndPoint { get; private set; }
        public string LastError { get; private set; }
        public bool IsMapped => _mapping != null && _device != null;

        public async Task<bool> TryMapAsync(int internalPort, int externalPort, string description, CancellationToken cancel)
        {
            try
            {
                var discoverer = new NatDiscoverer();

                // First try UPnP, then fall back to NAT-PMP
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(6));
                    _device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts)
                        .ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result : null);
                }

                if (_device == null)
                {
                    using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    _device = await discoverer.DiscoverDeviceAsync(PortMapper.Pmp, cts2)
                        .ContinueWith(t => t.Status == TaskStatus.RanToCompletion ? t.Result : null);
                }

                if (_device == null)
                {
                    LastError = "No UPnP/NAT-PMP gateway found.";
                    return false;
                }

                var wanIp = await _device.GetExternalIPAsync();
                ExternalEndPoint = new IPEndPoint(wanIp, externalPort);

                // 1-hour lease; we auto-renew in the background.
                _mapping = new Mapping(Protocol.Tcp, internalPort, externalPort, 3600, description);
                await _device.CreatePortMapAsync(_mapping);

                _renewCts = new CancellationTokenSource();
                _ = Task.Run(() => RenewLoop(_renewCts.Token), _renewCts.Token);

                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _device = null;
                _mapping = null;
                return false;
            }
        }

        private async Task RenewLoop(CancellationToken ct)
        {
            var delay = TimeSpan.FromMinutes(30);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(delay, ct);
                    if (ct.IsCancellationRequested) break;
                    if (_device != null && _mapping != null)
                        await _device.CreatePortMapAsync(_mapping); // renew lease
                }
                catch { /* ignore */ }
            }
        }

        public async Task UnmapAsync()
        {
            try
            {
                _renewCts?.Cancel();
                if (_device != null && _mapping != null)
                    await _device.DeletePortMapAsync(_mapping);
            }
            catch { /* some routers fail delete; safe to ignore on shutdown */ }
            finally
            {
                _mapping = null;
                _device = null;
                _renewCts?.Dispose();
                _renewCts = null;
            }
        }

        public void Dispose()
        {
            try { UnmapAsync().GetAwaiter().GetResult(); } catch { }
        }

        // URL ACL / firewall helpers for binding http://+:PORT/
        public static bool EnsureUrlAcl(int port, out string error)
        {
            error = null;
            var url = $"http://+:{port}/";

            try
            {
                // First attempt (might succeed if already elevated)
                var (ok, err) = RunNetshAdd(url);
                if (ok || AlreadyExists(err)) return true;

                // Not elevated? re-run with UAC prompt
                if (!IsAdministrator())
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"http add urlacl url={url} user={Environment.UserDomainName}\\{Environment.UserName}",
                        UseShellExecute = true,          // required for Verb=runas
                        Verb = "runas",                  // UAC
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit();
                }

                // Try once more
                var (ok2, err2) = RunNetshAdd(url);
                if (ok2 || AlreadyExists(err2)) return true;

                error = string.IsNullOrWhiteSpace(err2) ? "Unknown error creating URL ACL." : err2;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool EnsureFirewallRule(int port, string name, out string error)
        {
            error = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall add rule name=\"{name} {port}\" dir=in action=allow protocol=TCP localport={port}",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit();
                return true; // netsh returns 0 even if rule already exists
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static (bool ok, string err) RunNetshAdd(string url)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"http add urlacl url={url} user={Environment.UserDomainName}\\{Environment.UserName}",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(4000);

                bool success = (p.ExitCode == 0) || AlreadyExists(output) || AlreadyExists(stderr);
                return (success, string.IsNullOrWhiteSpace(stderr) ? output : stderr);
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        private static bool AlreadyExists(string s)
            => !string.IsNullOrEmpty(s) &&
               (s.IndexOf("Error: 183", StringComparison.OrdinalIgnoreCase) >= 0 // already exists
             || s.IndexOf("The parameter is incorrect", StringComparison.OrdinalIgnoreCase) >= 0 // sometimes netsh wording
             || s.IndexOf("exists", StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool IsAdministrator()
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
