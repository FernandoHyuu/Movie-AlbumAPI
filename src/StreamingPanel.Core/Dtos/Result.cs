namespace StreamingPanel.Core.Dtos;

/// <summary>
/// Outcome type for operations that return no value. Lets services signal expected
/// failures through an <see cref="ErrorCode"/> instead of throwing.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public ErrorCode ErrorCode { get; }

    public string? ErrorMessage { get; }

    private protected Result(bool isSuccess, ErrorCode errorCode, string? errorMessage)
    {
        // Success and an error code are mutually exclusive; enforce it here so a
        // malformed Result can never reach a caller.
        if (isSuccess && errorCode != ErrorCode.None)
        {
            throw new ArgumentException("A successful result cannot carry an error code.", nameof(errorCode));
        }

        if (!isSuccess && errorCode == ErrorCode.None)
        {
            throw new ArgumentException("A failed result must carry a non-None error code.", nameof(errorCode));
        }

        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public static Result Success() => new(true, ErrorCode.None, null);

    public static Result Failure(ErrorCode errorCode, string? errorMessage = null) =>
        new(false, errorCode, errorMessage);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(ErrorCode errorCode, string? errorMessage = null) =>
        Result<T>.Failure(errorCode, errorMessage);
}

/// <summary>
/// Outcome type carrying a value on success or an <see cref="ErrorCode"/> on failure.
/// </summary>
public sealed class Result<T> : Result
{
    private readonly T _value;

    // Accessing the value of a failed result is a programming error, so throw rather
    // than hand back a default that could slip through unnoticed.
    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("Cannot access the value of a failed result.");

    private Result(bool isSuccess, T value, ErrorCode errorCode, string? errorMessage)
        : base(isSuccess, errorCode, errorMessage)
    {
        _value = value;
    }

    public static Result<T> Success(T value) => new(true, value, ErrorCode.None, null);

    public static new Result<T> Failure(ErrorCode errorCode, string? errorMessage = null) =>
        new(false, default!, errorCode, errorMessage);
}
