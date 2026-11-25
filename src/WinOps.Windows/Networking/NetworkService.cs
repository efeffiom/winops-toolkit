using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using WinOps.Core.Abstractions;
using WinOps.Core.Models;
using WinOps.Core.Safety;
using WinOps.Windows.Integration;

namespace WinOps.Windows.Networking;

[SupportedOSPlatform("windows")]
public sealed class NetworkService : INetworkService
{
    private readonly ActionSafetyEngine _safety;
    private readonly CommandRunner _commands;

    public NetworkService(ActionSafetyEngine safety, CommandRunner commands)
    {
        _safety = safety;
        _commands = commands;
    }

    public async Task<IReadOnlyList<NetworkAdapterInfo>> GetAdaptersAsync(CancellationToken ct = default) =>
        await Task.Run(CollectAdapters, ct);

    private static IReadOnlyList<NetworkAdapterInfo> CollectAdapters()
    {
        var result = new List<NetworkAdapterInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var properties = nic.GetIPProperties();
            var ipv4 = properties.UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .ToArray();
            var ipv6 = properties.UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal)
                .Select(a => a.Address.ToString())
                .ToArray();
            var gateway = properties.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "None";
            var dns = properties.DnsAddresses.Select(a => a.ToString()).ToArray();
            var mac = nic.GetPhysicalAddress().GetAddressBytes().Length > 0
                ? string.Join("-", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")))
                : "Unavailable";
            result.Add(new NetworkAdapterInfo(
                nic.Name,
                nic.NetworkInterfaceType.ToString(),
                nic.OperationalStatus.ToString(),
                nic.Speed,
                mac,
                ipv4,
                ipv6,
                dns,
                gateway,
                properties.DhcpServerAddresses.Count > 0 ? "Enabled" : "Disabled",
                properties.DnsSuffix)
            {
                IpConfigurationScript = $"netsh interface ip show config name=\"{nic.Name}\""
            });
        }
        return result.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task<IReadOnlyList<RouteEntry>> GetRoutesAsync(CancellationToken ct = default) =>
        _commands.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "route.exe"), ["print", "-4"], ct)
            .ContinueWith(t => ParseRoutes(t.Result.StandardOutput), ct);

    private static IReadOnlyList<RouteEntry> ParseRoutes(string output)
    {
        var routes = new List<RouteEntry>();
        var inTable = false;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Active Routes:", StringComparison.OrdinalIgnoreCase)) { inTable = true; continue; }
            if (line.StartsWith("Persistent Routes:", StringComparison.OrdinalIgnoreCase)) break;
            if (!inTable || string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || !IPAddress.TryParse(parts[0], out _)) continue;
            routes.Add(new RouteEntry(parts[0], parts[1], parts[2], parts[3], int.TryParse(parts[4], out var metric) ? metric : 0));
        }
        return routes;
    }

    public Task<IReadOnlyList<ArpEntry>> GetArpEntriesAsync(CancellationToken ct = default) =>
        _commands.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "arp.exe"), ["-a"], ct)
            .ContinueWith(t => ParseArp(t.Result.StandardOutput), ct);

    private static IReadOnlyList<ArpEntry> ParseArp(string output)
    {
        var entries = new List<ArpEntry>();
        string? currentInterface = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Interface:", StringComparison.OrdinalIgnoreCase))
            {
                currentInterface = line[(line.IndexOf(':') + 1)..].Trim();
                continue;
            }
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && IPAddress.TryParse(parts[0], out _) && parts[1].Count(c => c == '-') == 5)
                entries.Add(new ArpEntry(parts[0], parts[1], currentInterface ?? "Unknown"));
        }
        return entries;
    }

    public async Task<IReadOnlyList<NetworkConnectionInfo>> GetConnectionsAsync(CancellationToken ct = default) =>
        await Task.Run(() =>
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var list = new List<NetworkConnectionInfo>();
            foreach (var c in properties.GetActiveTcpConnections())
                list.Add(new NetworkConnectionInfo("TCP", c.LocalEndPoint.Address.ToString(), c.LocalEndPoint.Port.ToString(),
                    c.RemoteEndPoint.Address.ToString(), c.RemoteEndPoint.Port.ToString(), c.State.ToString(), 0));
            foreach (var l in properties.GetActiveTcpListeners())
                list.Add(new NetworkConnectionInfo("TCP", l.Address.ToString(), l.Port.ToString(), "*", "*", "Listen", 0));
            foreach (var u in properties.GetActiveUdpListeners())
                list.Add(new NetworkConnectionInfo("UDP", u.Address.ToString(), u.Port.ToString(), "*", "*", "Listen", 0));
            return list;
        }, ct);

    public async Task<WifiInfo?> GetWifiInfoAsync(CancellationToken ct = default)
    {
        var result = await _commands.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netsh.exe"), ["wlan", "show", "interfaces"], ct, timeout: TimeSpan.FromSeconds(20));
        if (!result.Success && string.IsNullOrWhiteSpace(result.StandardOutput)) return null;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in result.StandardOutput.Split('\n'))
        {
            var line = raw.Trim();
            var colon = line.IndexOf(':');
            if (colon > 0) fields[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        var ssid = fields.GetValueOrDefault("SSID", string.Empty);
        if (string.IsNullOrWhiteSpace(ssid)) return null;
        return new WifiInfo(
            ssid,
            fields.GetValueOrDefault("Signal", "Unavailable"),
            fields.GetValueOrDefault("Authentication", "Unavailable"),
            fields.GetValueOrDefault("Cipher", "Unavailable"),
            fields.GetValueOrDefault("Channel", "Unavailable"),
            fields.GetValueOrDefault("State", "Unavailable"));
    }

    public async Task<PingResult> PingAsync(string host, int count = 4, CancellationToken ct = default)
    {
        var address = await ResolveFirstAddressAsync(host, ct);
        if (address is null)
            return new PingResult(host, string.Empty, false, count, 0, null, null, null, $"Could not resolve '{host}'.");

        var times = new List<long>();
        using var ping = new Ping();
        var options = new PingOptions(64, true);
        var buffer = Enumerable.Repeat((byte)0x61, 32).ToArray();
        for (var i = 0; i < Math.Clamp(count, 1, 20); i++)
        {
            ct.ThrowIfCancellationRequested();
            var reply = await ping.SendPingAsync(address, TimeSpan.FromSeconds(3), buffer, options, ct);
            if (reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
        }
        return new PingResult(
            host,
            address.ToString(),
            times.Count > 0,
            count,
            times.Count,
            times.Count > 0 ? times.Min() : null,
            times.Count > 0 ? times.Max() : null,
            times.Count > 0 ? times.Average() : null,
            times.Count == 0 ? "No replies received." : string.Empty);
    }

    public async Task<IReadOnlyList<TracerouteHop>> TracerouteAsync(string host, int maxHops = 30, CancellationToken ct = default)
    {
        var address = await ResolveFirstAddressAsync(host, ct);
        if (address is null) return [new TracerouteHop(0, string.Empty, null, $"Could not resolve '{host}'.")];
        var hops = new List<TracerouteHop>();
        using var ping = new Ping();
        var buffer = Enumerable.Repeat((byte)0x61, 32).ToArray();
        for (var ttl = 1; ttl <= Math.Clamp(maxHops, 1, 64); ttl++)
        {
            ct.ThrowIfCancellationRequested();
            var reply = await ping.SendPingAsync(address, TimeSpan.FromSeconds(3), buffer, new PingOptions(ttl, true), ct);
            var hopAddress = reply.Address?.ToString() ?? "*";
            if (reply.Status == IPStatus.Success || reply.Status == IPStatus.DestinationHostUnreachable || reply.Status == IPStatus.DestinationNetworkUnreachable)
            {
                hops.Add(new TracerouteHop(ttl, hopAddress, reply.Status == IPStatus.Success ? reply.RoundtripTime : null, string.Empty));
                if (address.Equals(reply.Address)) break;
            }
            else
            {
                hops.Add(new TracerouteHop(ttl, "*", null, reply.Status.ToString()));
            }
        }
        return hops;
    }

    private static async Task<IPAddress?> ResolveFirstAddressAsync(string host, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        if (IPAddress.TryParse(host, out var direct)) return direct;
        var addresses = await Dns.GetHostAddressesAsync(host, ct);
        return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault();
    }

    public async Task<IReadOnlyList<DnsLookupResult>> ResolveDnsAsync(string host, CancellationToken ct = default)
    {
        var results = new List<DnsLookupResult>();
        if (string.IsNullOrWhiteSpace(host)) return results;
        try
        {
            var entry = await Dns.GetHostEntryAsync(host, ct);
            results.Add(new DnsLookupResult(host, "A/AAAA", entry.AddressList.Select(a => a.ToString()).ToArray(), string.Empty));
            results.Add(new DnsLookupResult(host, "Aliases", entry.Aliases, string.Empty));
        }
        catch (Exception ex)
        {
            results.Add(new DnsLookupResult(host, "A/AAAA", [], ex.Message));
        }

        foreach (var type in new[] { "MX", "NS", "TXT" })
        {
            var command = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nslookup.exe");
            var result = await _commands.RunAsync(command, ["-type=" + type, host], ct, timeout: TimeSpan.FromSeconds(15));
            var records = result.StandardOutput.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Contains(type, StringComparison.OrdinalIgnoreCase) || l.Contains("mail exchanger", StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .ToArray();
            results.Add(new DnsLookupResult(host, type, records, result.Success ? string.Empty : result.StandardError));
        }
        return results;
    }

    public async Task<PortTestResult> TestPortAsync(string host, int port, int timeoutMs = 3000, CancellationToken ct = default)
    {
        var started = DateTimeOffset.Now;
        if (port is < 1 or > 65535) return new PortTestResult(host, port, false, "Port must be between 1 and 65535.", 0);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 100, 60000)));
            var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token);
            using var client = new TcpClient();
            await client.ConnectAsync(addresses, port, timeout.Token);
            return new PortTestResult(host, port, true, string.Empty, (long)(DateTimeOffset.Now - started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new PortTestResult(host, port, false, "Connection timed out.", (long)(DateTimeOffset.Now - started).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            return new PortTestResult(host, port, false, ex.Message, (long)(DateTimeOffset.Now - started).TotalMilliseconds);
        }
    }

    public async Task<string> GetPublicIpAsync(CancellationToken ct = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var url = attempt == 0 ? "https://api.ipify.org" : "https://icanhazip.com";
                var text = await client.GetStringAsync(url, ct);
                return text.Trim();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Try the fallback provider.
            }
        }
        return "Unavailable (no outbound internet access)";
    }

    public Task<ActionExecutionResult> RunNetworkCommandAsync(
        string action,
        string? adapterName,
        Func<ActionDefinition, Task<bool>> confirmAsync,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var normalized = action.ToLowerInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);
        var (actionId, executable, args) = normalized switch
        {
            "flushdns" => ("network.flushdns", Path.Combine(system, "ipconfig.exe"), new[] { "/flushdns" }),
            "registerdns" => ("network.registerdns", Path.Combine(system, "ipconfig.exe"), new[] { "/registerdns" }),
            "release" => ("network.release", Path.Combine(system, "ipconfig.exe"), adapterName is null ? new[] { "/release" } : new[] { "/release", adapterName }),
            "renew" => ("network.renew", Path.Combine(system, "ipconfig.exe"), adapterName is null ? new[] { "/renew" } : new[] { "/renew", adapterName }),
            "winsock" => ("network.winsock", Path.Combine(system, "netsh.exe"), new[] { "winsock", "reset" }),
            "tcpip" => ("network.tcpip", Path.Combine(system, "netsh.exe"), new[] { "int", "ip", "reset" }),
            "enableadapter" or "disableadapter" => ("network.adapter", Path.Combine(system, "netsh.exe"),
                new[] { "interface", "set", "interface", $"name=\"{adapterName}\"", $"admin={(normalized.StartsWith("enable") ? "enabled" : "disabled")}" }),
            _ => throw new InvalidOperationException($"Unsupported network action '{action}'.")
        };

        return _safety.ExecuteAsync(
            actionId,
            async token =>
            {
                var result = await _commands.RunAsync(executable, args, token, progress, TimeSpan.FromMinutes(2));
                return result.Success
                    ? ActionExecutionResult.Ok($"{action} completed.", result.CombinedOutput, $"{executable} {string.Join(' ', args)}", verified: false)
                    : ActionExecutionResult.Fail($"{action} failed.", result.StandardError, result.CombinedOutput, $"{executable} {string.Join(' ', args)}");
            },
            confirmAsync,
            progress,
            ct);
    }

    public Task<ActionExecutionResult> SetDnsServersAsync(string adapterName, IReadOnlyList<string> dnsServers, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default)
    {
        if (dnsServers.Count == 0 || dnsServers.Count > 4 || dnsServers.Any(d => !IPAddress.TryParse(d, out _)))
            return Task.FromResult(ActionExecutionResult.Fail("Provide between one and four valid DNS server IP addresses.", "Invalid DNS configuration"));

        var args = new List<string> { "interface", "ip", "set", "dnsservers", $"name=\"{adapterName}\"", "static", dnsServers[0], "primary", "validate=no" };
        var index = 1;
        foreach (var address in dnsServers.Skip(1))
            args.AddRange(["interface", "ip", "add", "dnsservers", $"name=\"{adapterName}\"", address, $"index={++index}", "validate=no"]);

        return ExecuteNetshSequenceAsync("network.setdns", args, $"DNS servers set to {string.Join(", ", dnsServers)} on {adapterName}.", confirmAsync, ct);
    }

    public Task<ActionExecutionResult> SetStaticIpAsync(string adapterName, string ipAddress, string prefixLength, string gateway, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(ipAddress, out _) || !int.TryParse(prefixLength, out var prefix) || prefix is < 0 or > 32 || !IPAddress.TryParse(gateway, out _))
            return Task.FromResult(ActionExecutionResult.Fail("Invalid static IP configuration. Supply a valid address, prefix length (0-32) and gateway.", "Validation failed"));
        var mask = PrefixToMask(prefix);
        var args = new[] { "interface", "ip", "set", "address", $"name=\"{adapterName}\"", "static", ipAddress, mask, gateway, "gwmetric=1", "validate=no" };
        return ExecuteNetshSequenceAsync("network.setstaticip", args, $"Static address {ipAddress}/{prefix} applied to {adapterName}.", confirmAsync, ct);
    }

    public Task<ActionExecutionResult> RestoreDhcpAsync(string adapterName, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct = default)
    {
        var args = new[]
        {
            "interface", "ip", "set", "address", $"name=\"{adapterName}\"", "dhcp",
            "interface", "ip", "set", "dnsservers", $"name=\"{adapterName}\"", "dhcp"
        };
        return ExecuteNetshSequenceAsync("network.restoredhcp", args, $"DHCP restored on {adapterName}.", confirmAsync, ct);
    }

    private Task<ActionExecutionResult> ExecuteNetshSequenceAsync(string actionId, IReadOnlyList<string> tokens, string successMessage, Func<ActionDefinition, Task<bool>> confirmAsync, CancellationToken ct)
    {
        // netsh accepts only one command per invocation, so split the token list at each verb group.
        var chunks = SplitNetshCommands(tokens);
        return _safety.ExecuteAsync(
            actionId,
            async token =>
            {
                var output = new List<string>();
                foreach (var chunk in chunks)
                {
                    var result = await _commands.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netsh.exe"), chunk, token, timeout: TimeSpan.FromMinutes(1));
                    output.Add(result.CombinedOutput);
                    if (!result.Success) return ActionExecutionResult.Fail("Network configuration failed.", result.StandardError, string.Join(Environment.NewLine, output), "netsh " + string.Join(' ', chunk));
                }
                return ActionExecutionResult.Ok(successMessage, string.Join(Environment.NewLine, output), "netsh " + string.Join(' ', tokens), verified: false);
            },
            confirmAsync,
            ct: ct);
    }

    private static IReadOnlyList<string[]> SplitNetshCommands(IReadOnlyList<string> tokens)
    {
        var commands = new List<string[]>();
        var current = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (current.Count > 0 && tokens[i].Equals("interface", StringComparison.OrdinalIgnoreCase))
            {
                commands.Add(current.ToArray());
                current.Clear();
            }
            current.Add(tokens[i]);
        }
        if (current.Count > 0) commands.Add(current.ToArray());
        return commands;
    }

    private static string PrefixToMask(int prefix)
    {
        var value = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        return new IPAddress(BitConverter.GetBytes(value).Reverse().ToArray()).ToString();
    }
}
