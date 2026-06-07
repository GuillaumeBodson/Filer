using System.Text;
using Filer.Modules.Documents.Files;
using FluentAssertions;
using Xunit;

namespace Filer.Modules.Documents.Tests.Files;

/// <summary>
/// The sniffing table itself (05-security.md): each allowed type accepts its own
/// magic bytes and rejects content that contradicts the declaration.
/// </summary>
public sealed class FileSignaturesTests
{
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0];
    private static readonly byte[] ZipHeader = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00];

    public static TheoryData<string, byte[]> MatchingSamples => new()
    {
        { "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.7") },
        { "image/png", PngHeader },
        { "image/jpeg", JpegHeader },
        { "image/webp", Encoding.ASCII.GetBytes("RIFFWEBPVP8 ") },
        { "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ZipHeader },
        { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ZipHeader },
        { "application/vnd.openxmlformats-officedocument.presentationml.presentation", ZipHeader },
        { "text/plain", Encoding.UTF8.GetBytes("plain text, no magic") },
        { "text/markdown", Encoding.UTF8.GetBytes("# heading") },
    };

    [Theory]
    [MemberData(nameof(MatchingSamples))]
    public void Matches_WhenContentCorroboratesDeclaredType_ReturnsTrue(string mediaType, byte[] sample) =>
        FileSignatures.Matches(mediaType, sample).Should().BeTrue();

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void Matches_WhenContentContradictsDeclaredBinaryType_ReturnsFalse(string mediaType) =>
        FileSignatures.Matches(mediaType, Encoding.ASCII.GetBytes("just some text")).Should().BeFalse();

    [Fact]
    public void Matches_WhenTextDeclarationHidesBinaryContent_ReturnsFalse() =>
        FileSignatures.Matches("text/plain", [0x68, 0x69, 0x00, 0x68, 0x69]).Should().BeFalse();

    [Fact]
    public void Matches_WhenRiffContainerIsNotWebP_ReturnsFalse() =>
        FileSignatures.Matches("image/webp", Encoding.ASCII.GetBytes("RIFFWAVEfmt ")).Should().BeFalse();

    [Fact]
    public void Matches_WhenTypeHasNoRegisteredSignature_FailsClosed() =>
        // Sniffing is mandatory (05): a type without a signature can never pass,
        // whatever its bytes. Startup validation keeps such types out of the
        // allow-list in the first place; this is the backstop behind it.
        FileSignatures.Matches("application/x-custom", [0x01, 0x02]).Should().BeFalse();

    [Fact]
    public void IsKnown_ForEveryDefaultAllowedType_ReturnsTrue()
    {
        // Pins the invariant the startup validation relies on: the shipped
        // defaults and the sniffing table cannot drift apart unnoticed.
        foreach (string mediaType in new DocumentsOptions().AllowedContentTypes)
        {
            FileSignatures.IsKnown(mediaType).Should().BeTrue(
                $"the default allow-list entry '{mediaType}' must have a registered signature");
        }
    }

    [Fact]
    public void IsKnown_ForTypeWithoutSignature_ReturnsFalse() =>
        FileSignatures.IsKnown("application/zip").Should().BeFalse();
}
