using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

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
            // Use a minimal DNS query over UDP for MX records.
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
    /// Note: .NET doesn't have built-in MX lookup in System.Net.Dns.
    /// This implementation performs a minimal DNS query over UDP.
    /// </summary>
    private static async Task<string[]> GetMxRecordsAsync(string domain, CancellationToken cancellationToken)
    {
        var nameServers = GetNameServers();
        if (nameServers.Length == 0)
        {
            return Array.Empty<string>();
        }

        foreach (var nameServer in nameServers)
        {
            var records = await QueryMxRecordsAsync(domain, nameServer, cancellationToken);
            if (records.Length > 0)
            {
                return records;
            }
        }

        return Array.Empty<string>();
    }

    private static async Task<string[]> QueryMxRecordsAsync(
        string domain,
        IPEndPoint nameServer,
        CancellationToken cancellationToken)
    {
        using var client = new UdpClient();
        client.Connect(nameServer);

        var query = BuildMxQuery(domain, out var queryId);
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendAsync(query, query.Length);

        var response = await client.ReceiveAsync(cancellationToken);
        return ParseMxResponse(response.Buffer, queryId);
    }

    internal static byte[] BuildMxQuery(string domain, out ushort queryId)
    {
        queryId = (ushort)Random.Shared.Next(ushort.MinValue, ushort.MaxValue + 1);

        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        // Header
        WriteUInt16(writer, queryId);       // ID
        WriteUInt16(writer, 0x0100);        // Flags: standard query, recursion desired
        WriteUInt16(writer, 1);             // QDCOUNT
        WriteUInt16(writer, 0);             // ANCOUNT
        WriteUInt16(writer, 0);             // NSCOUNT
        WriteUInt16(writer, 0);             // ARCOUNT

        // Question
        WriteQName(writer, domain);
        WriteUInt16(writer, 15);            // QTYPE = MX
        WriteUInt16(writer, 1);             // QCLASS = IN

        return buffer.ToArray();
    }

    internal static string[] ParseMxResponse(byte[] message, ushort queryId)
    {
        if (message.Length < 12)
        {
            return Array.Empty<string>();
        }

        var offset = 0;
        var responseId = ReadUInt16(message, ref offset);
        if (responseId != queryId)
        {
            return Array.Empty<string>();
        }

        var flags = ReadUInt16(message, ref offset);
        var isResponse = (flags & 0x8000) != 0;
        var rcode = flags & 0x000F;
        if (!isResponse || rcode != 0)
        {
            return Array.Empty<string>();
        }

        var qdCount = ReadUInt16(message, ref offset);
        var anCount = ReadUInt16(message, ref offset);
        var nsCount = ReadUInt16(message, ref offset);
        var arCount = ReadUInt16(message, ref offset);

        // Skip questions
        for (var i = 0; i < qdCount; i++)
        {
            SkipName(message, ref offset);
            offset += 4; // QTYPE + QCLASS
        }

        var mxRecords = new List<string>();

        for (var i = 0; i < anCount; i++)
        {
            SkipName(message, ref offset);
            var type = ReadUInt16(message, ref offset);
            var @class = ReadUInt16(message, ref offset);
            offset += 4; // TTL
            var rdLength = ReadUInt16(message, ref offset);

            if (type == 15 && @class == 1)
            {
                offset += 2; // Preference
                var exchange = ReadName(message, ref offset);
                if (!string.IsNullOrWhiteSpace(exchange))
                {
                    mxRecords.Add(exchange.TrimEnd('.'));
                }
            }
            else
            {
                offset += rdLength;
            }
        }

        _ = nsCount;
        _ = arCount;

        return mxRecords.ToArray();
    }

    private static void WriteQName(BinaryWriter writer, string domain)
    {
        var labels = domain.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var label in labels)
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }

        writer.Write((byte)0);
    }

    private static void SkipName(byte[] message, ref int offset)
    {
        ReadName(message, ref offset);
    }

    private static string ReadName(byte[] message, ref int offset)
    {
        var name = new StringBuilder();
        var jumped = false;
        var originalOffset = offset;

        while (offset < message.Length)
        {
            var length = message[offset++];
            if (length == 0)
            {
                break;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (offset >= message.Length)
                {
                    break;
                }

                var pointer = ((length & 0x3F) << 8) | message[offset++];
                if (!jumped)
                {
                    originalOffset = offset;
                }

                offset = pointer;
                jumped = true;
                continue;
            }

            if (offset + length > message.Length)
            {
                break;
            }

            if (name.Length > 0)
            {
                name.Append('.');
            }

            name.Append(Encoding.ASCII.GetString(message, offset, length));
            offset += length;
        }

        if (jumped)
        {
            offset = originalOffset;
        }

        return name.ToString();
    }

    private static ushort ReadUInt16(byte[] message, ref int offset)
    {
        if (offset + 1 >= message.Length)
        {
            offset = message.Length;
            return 0;
        }

        var value = (ushort)((message[offset] << 8) | message[offset + 1]);
        offset += 2;
        return value;
    }

    private static void WriteUInt16(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)(value & 0xFF));
    }

    private static IPEndPoint[] GetNameServers()
    {
        var servers = new List<IPEndPoint>();

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
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
                    if (parts.Length < 2)
                    {
                        continue;
                    }

                    if (IPAddress.TryParse(parts[1], out var ip))
                    {
                        servers.Add(new IPEndPoint(ip, 53));
                    }
                }
            }
            catch
            {
                // Ignore and fall back to public resolvers.
            }
        }

        if (servers.Count == 0)
        {
            servers.Add(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53));
            servers.Add(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53));
        }

        return servers.ToArray();
    }
}
