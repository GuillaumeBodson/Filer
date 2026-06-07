namespace Filer.Modules.Documents.Files;

/// <summary>
/// Content sniffing for the V1 allow-list: a file is accepted only when its magic
/// bytes corroborate the declared content type — declared MIME alone is
/// client-controlled and therefore untrusted (05-security.md, upload security;
/// 04-non-functional.md, supported types).
/// </summary>
internal static class FileSignatures
{
    /// <summary>Bytes to sample from the head of the file for sniffing.</summary>
    public const int SampleLength = 512;

    private static readonly byte[] Pdf = "%PDF"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] WebP = "WEBP"u8.ToArray();

    // The OOXML formats (.docx/.xlsx/.pptx) are ZIP containers (PK\x03\x04).
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>
    /// Whether the sampled head of the file is plausible for the declared media
    /// type. Types without a registered signature (added later purely through
    /// configuration, 04) pass: for those the declared type is the only evidence
    /// available, and rejecting them would make the allow-list non-extensible
    /// without code changes.
    /// </summary>
    public static bool Matches(string mediaType, ReadOnlySpan<byte> sample) =>
        mediaType switch
        {
            KnownMediaTypes.Pdf => sample.StartsWith(Pdf),
            KnownMediaTypes.Png => sample.StartsWith(Png),
            KnownMediaTypes.Jpeg => sample.StartsWith(Jpeg),
            KnownMediaTypes.Webp => IsWebP(sample),
            KnownMediaTypes.Docx or KnownMediaTypes.Xlsx or KnownMediaTypes.Pptx =>
                sample.StartsWith(Zip),
            KnownMediaTypes.PlainText or KnownMediaTypes.Markdown => LooksLikeText(sample),
            _ => true,
        };

    /// <summary>WebP: RIFF container with the WEBP fourcc at offset 8.</summary>
    private static bool IsWebP(ReadOnlySpan<byte> sample) =>
        sample.Length >= 12
        && sample.StartsWith(Riff)
        && sample.Slice(8, 4).SequenceEqual(WebP);

    /// <summary>
    /// Text has no magic number; reject samples carrying NUL bytes — the cheap,
    /// reliable tell of binary content smuggled under a text declaration.
    /// </summary>
    private static bool LooksLikeText(ReadOnlySpan<byte> sample) =>
        sample.IndexOf((byte)0) < 0;
}
