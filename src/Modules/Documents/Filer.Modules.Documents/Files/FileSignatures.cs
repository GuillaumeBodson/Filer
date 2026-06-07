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
    /// type. Fail-closed: a type without a registered signature never passes —
    /// sniffing is mandatory for every accepted upload (05-security.md), so the
    /// allow-list may only contain types this table knows. That invariant is
    /// enforced at startup (<c>DocumentsModule</c>) via <see cref="IsKnown"/>;
    /// this default is the defense-in-depth backstop behind it.
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
            _ => false,
        };

    /// <summary>
    /// Whether <see cref="Matches"/> has a registered signature for the media
    /// type. Kept adjacent to the switch above so the two cannot drift unnoticed;
    /// the defaults-stay-known invariant is also pinned by a unit test.
    /// </summary>
    public static bool IsKnown(string mediaType) =>
        mediaType switch
        {
            KnownMediaTypes.Pdf or
            KnownMediaTypes.Png or
            KnownMediaTypes.Jpeg or
            KnownMediaTypes.Webp or
            KnownMediaTypes.Docx or
            KnownMediaTypes.Xlsx or
            KnownMediaTypes.Pptx or
            KnownMediaTypes.PlainText or
            KnownMediaTypes.Markdown => true,
            _ => false,
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
