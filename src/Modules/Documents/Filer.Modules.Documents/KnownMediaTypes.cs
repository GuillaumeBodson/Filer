using System.Net.Mime;

namespace Filer.Modules.Documents;

/// <summary>
/// Single source for the MIME strings the module spells in more than one place —
/// the <see cref="DocumentsOptions"/> defaults and the <c>FileSignatures</c>
/// sniffing table. A typo'd string is invisible to the compiler, and in the
/// sniffing table it would silently skip sniffing for that type (unknown types
/// pass by design); referencing one constant from both sites makes that failure
/// impossible. Reuses the BCL's <see cref="MediaTypeNames"/> where it defines the
/// value; that class is static and cannot be extended, so the OOXML trio it lacks
/// is defined once here. The allow-list itself stays plain configuration strings,
/// but every configured type must carry a registered signature in
/// <c>FileSignatures</c> — enforced at startup — so narrowing the list is pure
/// configuration while expanding it is a deliberate (small) code change
/// (04-non-functional.md, 05-security.md).
/// </summary>
internal static class KnownMediaTypes
{
    public const string Pdf = MediaTypeNames.Application.Pdf;
    public const string Png = MediaTypeNames.Image.Png;
    public const string Jpeg = MediaTypeNames.Image.Jpeg;
    public const string Webp = MediaTypeNames.Image.Webp;
    public const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string Pptx = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
    public const string PlainText = MediaTypeNames.Text.Plain;
    public const string Markdown = MediaTypeNames.Text.Markdown;
}
