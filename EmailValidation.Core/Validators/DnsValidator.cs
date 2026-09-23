using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DnsClient;

namespace EmailValidation.Core.Validators;

/// <summary>
/// Layer 3 & 4: DNS validation - domain existence and mail routing.
/// NO FALLBACKS. If there's no MX record, mail cannot be delivered.
/// </summary>
public static class DnsValidator
{
    /// <summary>
    /// Layer 3: Check if domain exists (DNS A or AAAA record).
    /// </summary>
    public static async Task<ValidationResult> ValidateDomainExistsAsync(
        string domain,
        EmailValidatorOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotExist,
                "Domain cannot be empty");
        }

        try
        {
            // SECURITY: Add timeout to prevent hanging requests
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

            linkedCts.Token.ThrowIfCancellationRequested();

            var addresses = await Dns.GetHostAddressesAsync(domain, linkedCts.Token);

            if (addresses.Length == 0)
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.DomainDoesNotExist,
                    $"Domain '{domain}' has no A or AAAA records");
            }

            // SECURITY: Block private/internal IP addresses (SSRF protection).
            // Distinct failure reason from "no records at all" - this is a hard
            // block that must never be overridden by a later pipeline layer
            // (e.g. a successful MX check), unlike genuine non-existence.
            if (!options.AllowInternalDomains)
            {
                foreach (var addr in addresses)
                {
                    if (addr.IsInternalOrPrivate())
                    {
                        return ValidationResult.Failure(
                            ValidationFailureReason.InternalAddressBlocked,
                            $"Domain '{domain}' resolves to internal/private address");
                    }
                }
            }

            // Domain exists
            return ValidationResult.Success(domain, string.Empty, domain);
        }
        catch (OperationCanceledException)
        {
            var isTimeout = !cancellationToken.IsCancellationRequested;
            var message = isTimeout
                ? $"DNS lookup timeout for domain '{domain}'"
                : $"DNS lookup canceled for domain '{domain}'";

            return ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotExist,
                message);
        }
        catch (SocketException)
        {
            return ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotExist,
                $"Domain '{domain}' does not exist (DNS lookup failed)");
        }
        catch (Exception ex)
        {
            var message = $"DNS lookup failed for domain '{domain}'";
            if (options.DetailedErrorMessages)
            {
                message += $" (Detail: {ex.Message})";
            }
            return ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotExist,
                message);
        }
    }

    /// <summary>
    /// Layer 4: Check if domain accepts mail (MX records).
    /// CRITICAL: No MX record = no mail delivery. Period.
    /// We do NOT fall back to A records (historical behavior, operationally unsafe).
    /// </summary>
    public static async Task<ValidationResult> ValidateMxRecordsAsync(
        string domain,
        EmailValidatorOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotAcceptMail,
                "Domain cannot be empty");
        }

        try
        {
            var mxRecords = await GetMxRecordsAsync(domain, options, cancellationToken);

            if (mxRecords.Length == 0)
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.DomainDoesNotAcceptMail,
                    $"Domain '{domain}' has no MX records - cannot accept mail");
            }

            // SECURITY: Block MX exchangers that resolve to private/internal addresses
            // (SSRF protection). Mirrors the same check on the domain's own A/AAAA
            // records - without this, a domain with no public A/AAAA record could
            // point mail at an internal host and never be screened, since MX success
            // can now stand on its own when the apex has no A/AAAA record.
            if (!options.AllowInternalDomains)
            {
                foreach (var exchange in mxRecords)
                {
                    if (await ResolvesToInternalAddressAsync(exchange, cancellationToken))
                    {
                        return ValidationResult.Failure(
                            ValidationFailureReason.InternalAddressBlocked,
                            $"Domain '{domain}' has an MX record ('{exchange}') that resolves to an internal/private address");
                    }
                }
            }

            // MX records found - domain accepts mail
            return ValidationResult.Success(domain, string.Empty, domain, mxRecords);
        }
        catch (OperationCanceledException)
        {
            var isTimeout = !cancellationToken.IsCancellationRequested;
            var message = isTimeout
                ? $"MX lookup timeout for domain '{domain}'"
                : $"MX lookup canceled for domain '{domain}'";

            return ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotAcceptMail,
                message);
        }
        catch (Exception ex)
        {
            var message = $"MX lookup failed for domain '{domain}'";
            if (options.DetailedErrorMessages)
            {
                message += $" (Detail: {ex.Message})";
            }
            return ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotAcceptMail,
                message);
        }
    }

    /// <summary>
    /// Resolves an MX exchange hostname and reports whether any resolved address
    /// is private/internal/loopback. Resolution failures are not treated as a
    /// block - an MX host we can't resolve isn't a confirmed internal target,
    /// it's just an unrelated lookup failure (e.g. transient DNS issue).
    /// </summary>
    private static async Task<bool> ResolvesToInternalAddressAsync(string exchange, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var addresses = await Dns.GetHostAddressesAsync(exchange, linkedCts.Token);
            return addresses.Any(a => a.IsInternalOrPrivate());
        }
        catch
        {
            // Can't resolve the exchange host at all - not a confirmed internal
            // target, so don't block on it here.
            return false;
        }
    }

    private static async Task<string[]> GetMxRecordsAsync(string domain, EmailValidatorOptions options, CancellationToken cancellationToken)
    {
        var lookupOptions = GetLookupClientOptions(options);
        var lookup = new LookupClient(lookupOptions);

        cancellationToken.ThrowIfCancellationRequested();

        var result = await lookup.QueryAsync(domain, QueryType.MX, cancellationToken: cancellationToken);

        if (result.HasError)
        {
            throw new Exception(result.ErrorMessage);
        }

        return result.Answers
            .MxRecords()
            .OrderBy(mx => mx.Preference)
            .Select(mx => mx.Exchange.Value.TrimEnd('.'))
            .ToArray();
    }

    private static LookupClientOptions GetLookupClientOptions(EmailValidatorOptions options)
    {
        var nameServers = GetNameServers(options);
        var lookupOptions = new LookupClientOptions(nameServers)
        {
            UseCache = false,
            Timeout = TimeSpan.FromSeconds(5),
            Retries = 2,
            ThrowDnsErrors = false
        };
        return lookupOptions;
    }

    private static NameServer[] GetNameServers(EmailValidatorOptions options)
    {
        var servers = new List<NameServer>();

        if (!string.IsNullOrWhiteSpace(options.PrimaryDnsServer) &&
            IPAddress.TryParse(options.PrimaryDnsServer, out var primary))
        {
            servers.Add(new NameServer(new IPEndPoint(primary, 53)));
        }

        if (!string.IsNullOrWhiteSpace(options.SecondaryDnsServer) &&
            IPAddress.TryParse(options.SecondaryDnsServer, out var secondary))
        {
            servers.Add(new NameServer(new IPEndPoint(secondary, 53)));
        }

        if (servers.Count == 0 && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
        {
            try
            {
                foreach (var line in File.ReadLines("/etc/resolv.conf"))
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("nameserver", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (parts.Length >= 2 && IPAddress.TryParse(parts[1], out var ip))
                    {
                        servers.Add(new NameServer(new IPEndPoint(ip, 53)));
                    }
                }
            }
            catch
            {
                // Ignore and fall back
            }
        }

        if (servers.Count == 0 && options.AllowPublicDnsFallback)
        {
            servers.Add(new NameServer(IPAddress.Parse("8.8.8.8")));
            servers.Add(new NameServer(IPAddress.Parse("1.1.1.1")));
        }

        if (servers.Count == 0)
        {
            // If strictly local or misconfigured environment with no public fallback allowed:
            // Fallback to safe defaults or throw. In production without config, this will throw.
            // We use DnsClient's default which queries system networks automatically if we just let it.
            // But we already parsed resolv.conf on Linux/Mac. On Windows it wouldn't find any.
            // Let's fallback to localhost if entirely empty.
            servers.Add(new NameServer(IPAddress.Loopback));
        }

        return servers.ToArray();
    }
}

internal static class IPAddressExtensions
{
    public static bool IsInternalOrPrivate(this IPAddress address)
    {
        if (address == null)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();

        // IPv6 mapped IPv4
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
            bytes = address.GetAddressBytes();
        }

        return IPAddress.IsLoopback(address) ||
               // Private ranges (RFC 1918)
               (bytes[0] == 10) ||
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               // Link-local / APIPA (169.254.0.0/16)
               (bytes[0] == 169 && bytes[1] == 254) ||
               // Cloud metadata endpoints
               (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) || // 100.64.0.0/10 (Carrier-grade NAT)
               // Multicast
               (bytes[0] >= 224 && bytes[0] <= 239) ||
               // Reserved/experimental
               (bytes[0] >= 240);
    }
}
