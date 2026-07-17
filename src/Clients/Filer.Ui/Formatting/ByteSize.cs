using System.Globalization;

namespace Filer.Ui.Formatting;

/// <summary>Human-readable byte counts for list and detail views (binary units).</summary>
public static class ByteSize
{
    public static string Format(long? bytes) => bytes switch
    {
        null or < 0 => "—",
        < 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        < 1024L * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):0.#} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024 * 1024):0.#} GB"),
    };
}
