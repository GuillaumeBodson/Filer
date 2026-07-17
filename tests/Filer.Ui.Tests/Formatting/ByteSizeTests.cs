using Filer.Ui.Formatting;
using FluentAssertions;
using Xunit;

namespace Filer.Ui.Tests.Formatting;

public sealed class ByteSizeTests
{
    [Theory]
    [InlineData(null, "—")]
    [InlineData(-1L, "—")]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(131072L, "128 KB")]
    [InlineData(2516582L, "2.4 MB")]
    [InlineData(1610612736L, "1.5 GB")]
    public void Formats_binary_units(long? bytes, string expected) =>
        ByteSize.Format(bytes).Should().Be(expected);
}
