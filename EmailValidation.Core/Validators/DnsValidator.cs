using System.Net;
using System.Net.Sockets;

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
    public static async Task<ValidationResult> ValidateDomainExistsAsync(string domain, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotExist,
                "Domain cannot be empty");
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(domain, cancellationToken);

            if (addresses.Length == 0)
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.DomainDoesNotExist,
                    $"Domain '{domain}' has no A or AAAA records");
            }

            // Domain exists
            return ValidationResult.Success(domain, string.Empty, domain);
        }
        catch (SocketException)
        {
            return ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotExist,
                $"Domain '{domain}' does not exist (DNS lookup failed)");
        }
        catch (Exception ex)
        {
            return ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotExist,
                $"DNS lookup failed for domain '{domain}': {ex.Message}");
        }
    }

    /// <summary>
    /// Layer 4: Check if domain accepts mail (MX records).
    /// CRITICAL: No MX record = no mail delivery. Period.
    /// We do NOT fall back to A records (historical behavior, operationally unsafe).
    /// </summary>
    public static async Task<ValidationResult> ValidateMxRecordsAsync(string domain, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotAcceptMail,
                "Domain cannot be empty");
        }

        try
        {
            // Use DnsClient library or platform DNS
            // For now, we'll use a simple approach with nslookup/dig equivalent
            var mxRecords = await GetMxRecordsAsync(domain, cancellationToken);

            if (mxRecords.Length == 0)
            {
                return ValidationResult.Failure(
                    ValidationFailureReason.DomainDoesNotAcceptMail,
                    $"Domain '{domain}' has no MX records - cannot accept mail");
            }

            // MX records found - domain accepts mail
            return ValidationResult.Success(domain, string.Empty, domain, mxRecords);
        }
        catch (Exception ex)
        {
            return ValidationResult.Failure(
                ValidationFailureReason.DomainDoesNotAcceptMail,
                $"MX lookup failed for domain '{domain}': {ex.Message}");
        }
    }

    /// <summary>
    /// Get MX records for a domain.
    /// Note: .NET doesn't have built-in MX lookup in System.Net.Dns,
    /// so we need to use either:
    /// 1. DnsClient library (recommended)
    /// 2. Platform tools (dig/nslookup via Process)
    /// 3. Manual DNS query implementation
    ///
    /// For production, use DnsClient NuGet package.
    /// This is a simplified implementation using platform tools.
    /// </summary>
    private static async Task<string[]> GetMxRecordsAsync(string domain, CancellationToken cancellationToken)
    {
        // TODO: Replace with DnsClient library for production
        // For now, we'll use a process-based approach as a placeholder

        // On Linux/macOS, use 'dig'
        // On Windows, use 'nslookup'

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return await GetMxRecordsViaDig(domain, cancellationToken);
        }
        else if (OperatingSystem.IsWindows())
        {
            return await GetMxRecordsViaNslookup(domain, cancellationToken);
        }
        else
        {
            throw new PlatformNotSupportedException("MX record lookup not supported on this platform");
        }
    }

    private static async Task<string[]> GetMxRecordsViaDig(string domain, CancellationToken cancellationToken)
    {
        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dig",
            Arguments = $"+short MX {domain}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(processInfo);
        if (process == null)
        {
            return Array.Empty<string>();
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            return Array.Empty<string>();
        }

        // Parse dig output: "10 mail.example.com."
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mxRecords = lines
            .Select(line =>
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2 ? parts[1].TrimEnd('.') : null;
            })
            .Where(mx => !string.IsNullOrWhiteSpace(mx))
            .ToArray();

        return mxRecords!;
    }

    private static async Task<string[]> GetMxRecordsViaNslookup(string domain, CancellationToken cancellationToken)
    {
        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "nslookup",
            Arguments = $"-type=MX {domain}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(processInfo);
        if (process == null)
        {
            return Array.Empty<string>();
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        // Parse nslookup output - very basic parsing
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mxRecords = new List<string>();

        foreach (var line in lines)
        {
            // Look for lines like "mail exchanger = 10 mail.example.com"
            if (line.Contains("mail exchanger", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split('=', StringSplitOptions.TrimEntries);
                if (parts.Length >= 2)
                {
                    var mxParts = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (mxParts.Length >= 2)
                    {
                        mxRecords.Add(mxParts[1].TrimEnd('.'));
                    }
                }
            }
        }

        return mxRecords.ToArray();
    }
}
