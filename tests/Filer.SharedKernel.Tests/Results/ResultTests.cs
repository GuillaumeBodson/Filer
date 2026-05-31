using FluentAssertions;
using Filer.SharedKernel.Results;
using Xunit;

namespace Filer.SharedKernel.Tests.Results;

public sealed class ResultTests
{
    [Fact]
    public void Success_IsSuccessfulAndCarriesNoError()
    {
        Result result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_IsFailedAndCarriesTheError()
    {
        Error error = Error.NotFound();

        Result result = Result.Failure(error);

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void SuccessOfT_ExposesTheValue()
    {
        Result<int> result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void FailureOfT_IsFailedAndCarriesTheError()
    {
        Error error = Error.Validation("bad input");

        Result<int> result = Result.Failure<int>(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void FailureOfT_ReadingTheValue_Throws()
    {
        Result<int> result = Result.Failure<int>(Error.Unexpected());

        Action read = () => _ = result.Value;

        read.Should().Throw<InvalidOperationException>();
    }
}
