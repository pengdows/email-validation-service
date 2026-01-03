using EmailValidation.Core.Validators;
using FluentAssertions;
using System.Text;

namespace EmailValidation.Tests;

public class DnsValidatorTests
{
    [Fact]
    public void BuildMxQuery_UsesMxTypeAndInClass()
    {
        // Act
        var query = DnsValidator.BuildMxQuery("example.com", out _);

        // Assert
        query.Length.Should().BeGreaterThan(12);
        query[^4].Should().Be(0x00);
        query[^3].Should().Be(0x0F); // MX
        query[^2].Should().Be(0x00);
        query[^1].Should().Be(0x01); // IN
    }

    [Fact]
    public void ParseMxResponse_ReturnsMxRecords()
    {
        // Arrange
        var query = DnsValidator.BuildMxQuery("example.com", out var queryId);
        var response = BuildMxResponse(queryId);

        // Act
        var records = DnsValidator.ParseMxResponse(response, queryId);

        // Assert
        records.Should().ContainSingle()
            .Which.Should().Be("mail.example.com");
    }

    [Fact]
    public void ParseMxResponse_MismatchedId_ReturnsEmpty()
    {
        // Arrange
        var response = BuildMxResponse(1234);

        // Act
        var records = DnsValidator.ParseMxResponse(response, 4321);

        // Assert
        records.Should().BeEmpty();
    }

    [Fact]
    public void ParseMxResponse_TooShort_ReturnsEmpty()
    {
        // Act
        var records = DnsValidator.ParseMxResponse(new byte[5], 1);

        // Assert
        records.Should().BeEmpty();
    }

    [Fact]
    public void ParseMxResponse_NotAResponse_ReturnsEmpty()
    {
        // Arrange
        var response = BuildHeaderOnlyResponse(queryId: 10, flags: 0x0100);

        // Act
        var records = DnsValidator.ParseMxResponse(response, 10);

        // Assert
        records.Should().BeEmpty();
    }

    [Fact]
    public void ParseMxResponse_RcodeError_ReturnsEmpty()
    {
        // Arrange
        var response = BuildHeaderOnlyResponse(queryId: 11, flags: 0x8183);

        // Act
        var records = DnsValidator.ParseMxResponse(response, 11);

        // Assert
        records.Should().BeEmpty();
    }

    private static byte[] BuildMxResponse(ushort queryId)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        // Header
        WriteUInt16(writer, queryId); // ID
        WriteUInt16(writer, 0x8180);  // Standard query response, recursion available, no error
        WriteUInt16(writer, 1);       // QDCOUNT
        WriteUInt16(writer, 1);       // ANCOUNT
        WriteUInt16(writer, 0);       // NSCOUNT
        WriteUInt16(writer, 0);       // ARCOUNT

        // Question: example.com
        WriteQName(writer, "example.com");
        WriteUInt16(writer, 15);      // QTYPE = MX
        WriteUInt16(writer, 1);       // QCLASS = IN

        // Answer
        WriteUInt16(writer, 0xC00C);  // NAME pointer to offset 12 (start of question)
        WriteUInt16(writer, 15);      // TYPE = MX
        WriteUInt16(writer, 1);       // CLASS = IN
        writer.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // TTL

        using var rdata = new MemoryStream();
        using var rdataWriter = new BinaryWriter(rdata);
        WriteUInt16(rdataWriter, 10); // Preference
        WriteQName(rdataWriter, "mail.example.com");

        var rdataBytes = rdata.ToArray();
        WriteUInt16(writer, (ushort)rdataBytes.Length);
        writer.Write(rdataBytes);

        return buffer.ToArray();
    }

    private static byte[] BuildHeaderOnlyResponse(ushort queryId, ushort flags)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);

        WriteUInt16(writer, queryId); // ID
        WriteUInt16(writer, flags);
        WriteUInt16(writer, 0); // QDCOUNT
        WriteUInt16(writer, 0); // ANCOUNT
        WriteUInt16(writer, 0); // NSCOUNT
        WriteUInt16(writer, 0); // ARCOUNT

        return buffer.ToArray();
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

    private static void WriteUInt16(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)(value & 0xFF));
    }
}
