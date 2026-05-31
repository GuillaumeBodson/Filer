namespace Filer.SharedKernel.Results;

/// <summary>
/// Outcome of an operation that either succeeds or fails with an <see cref="Error"/>.
/// Feature services return this; endpoints map it to a typed HTTP result
/// (10-solution-structure.md).
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error is null)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error? Error { get; }

    public static Result Success() => new(true, null);

    public static Result Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value) => new(true, value, null);

    public static Result<T> Failure<T>(Error error) => new(false, default!, error);
}

/// <summary>A <see cref="Result"/> that carries a value on success.</summary>
public sealed class Result<T> : Result
{
    private readonly T _value;

    internal Result(bool isSuccess, T value, Error? error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("Cannot read the value of a failed result.");
}
