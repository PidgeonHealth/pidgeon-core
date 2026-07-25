// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core;

/// <summary>
/// Represents the result of an operation that can succeed or fail.
/// Provides explicit error handling without exceptions for business logic control flow.
/// </summary>
/// <typeparam name="T">The type of value returned on success</typeparam>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly Error _error;

    private Result(T value)
    {
        _value = value;
        _error = default;
        IsSuccess = true;
    }

    private Result(Error error)
    {
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    /// <summary>
    /// Gets a value indicating whether the operation was successful.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the value if the operation was successful.
    /// Throws InvalidOperationException if accessed when IsFailure is true.
    /// </summary>
    public T Value => IsSuccess 
        ? _value! 
        : throw new InvalidOperationException("Cannot access Value when Result is in failure state. Check IsSuccess first.");

    /// <summary>
    /// Gets the error if the operation failed.
    /// Throws InvalidOperationException if accessed when IsSuccess is true.
    /// </summary>
    public Error Error => IsFailure 
        ? _error 
        : throw new InvalidOperationException("Cannot access Error when Result is in success state. Check IsFailure first.");

    /// <summary>
    /// Creates a successful result with the specified value.
    /// </summary>
    /// <param name="value">The success value</param>
    /// <returns>A successful result</returns>
    public static Result<T> Success(T value) => new(value);

    /// <summary>
    /// Creates a failed result with the specified error.
    /// </summary>
    /// <param name="error">The error information</param>
    /// <returns>A failed result</returns>
    public static Result<T> Failure(Error error) => new(error);

    /// <summary>
    /// Creates a failed result with the specified error message.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <returns>A failed result</returns>
    public static Result<T> Failure(string message) => new(Error.Create(message));

    /// <summary>
    /// Transforms the result value using the specified function if successful.
    /// </summary>
    /// <typeparam name="TNext">The type of the transformed value</typeparam>
    /// <param name="func">The transformation function</param>
    /// <returns>A result containing the transformed value or the original error</returns>
    public Result<TNext> Map<TNext>(Func<T, TNext> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return IsSuccess ? Result<TNext>.Success(func(_value!)) : Result<TNext>.Failure(_error);
    }

    /// <summary>
    /// Chains another operation that returns a Result.
    /// </summary>
    /// <typeparam name="TNext">The type of the next result value</typeparam>
    /// <param name="func">The function that returns the next result</param>
    /// <returns>The result of the next operation or the original error</returns>
    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return IsSuccess ? func(_value!) : Result<TNext>.Failure(_error);
    }

    /// <summary>
    /// Executes an action if the result is successful, returning the original result.
    /// </summary>
    /// <param name="action">The action to execute</param>
    /// <returns>The original result</returns>
    public Result<T> OnSuccess(Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsSuccess) action(_value!);
        return this;
    }

    /// <summary>
    /// Executes an action if the result is a failure, returning the original result.
    /// </summary>
    /// <param name="action">The action to execute</param>
    /// <returns>The original result</returns>
    public Result<T> OnFailure(Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsFailure) action(_error);
        return this;
    }

    /// <summary>
    /// Returns the value if successful, or the default value if failed.
    /// </summary>
    /// <param name="defaultValue">The default value to return on failure</param>
    /// <returns>The result value or default value</returns>
    public T GetValueOrDefault(T defaultValue = default!) => IsSuccess ? _value! : defaultValue;

    /// <summary>
    /// Implicit conversion from T to Result&lt;T&gt;.
    /// </summary>
    /// <param name="value">The value to convert</param>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>
    /// Implicit conversion from Error to Result&lt;T&gt;.
    /// </summary>
    /// <param name="error">The error to convert</param>
    public static implicit operator Result<T>(Error error) => Failure(error);
}

/// <summary>
/// Represents the result of an operation that can succeed or fail (for void operations).
/// Provides explicit error handling without exceptions for business logic control flow.
/// </summary>
public readonly struct Result
{
    private readonly Error _error;

    private Result(Error error)
    {
        _error = error;
        IsSuccess = false;
    }

    private Result(bool success)
    {
        _error = default;
        IsSuccess = success;
    }

    /// <summary>
    /// Gets a value indicating whether the operation was successful.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the error if the operation failed.
    /// Throws InvalidOperationException if accessed when IsSuccess is true.
    /// </summary>
    public Error Error => IsFailure 
        ? _error 
        : throw new InvalidOperationException("Cannot access Error when Result is in success state. Check IsFailure first.");

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <returns>A successful result</returns>
    public static Result Success() => new(true);

    /// <summary>
    /// Creates a failed result with the specified error.
    /// </summary>
    /// <param name="error">The error information</param>
    /// <returns>A failed result</returns>
    public static Result Failure(Error error) => new(error);

    /// <summary>
    /// Creates a failed result with the specified error message.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <returns>A failed result</returns>
    public static Result Failure(string message) => new(Error.Create(message));

    /// <summary>
    /// Executes an action if the result is successful, returning the original result.
    /// </summary>
    /// <param name="action">The action to execute</param>
    /// <returns>The original result</returns>
    public Result OnSuccess(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsSuccess) action();
        return this;
    }

    /// <summary>
    /// Executes an action if the result is a failure, returning the original result.
    /// </summary>
    /// <param name="action">The action to execute</param>
    /// <returns>The original result</returns>
    public Result OnFailure(Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsFailure) action(_error);
        return this;
    }

    /// <summary>
    /// Implicit conversion from Error to Result.
    /// </summary>
    /// <param name="error">The error to convert</param>
    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>
/// Represents an error with contextual information.
/// </summary>
public readonly struct Error
{
    /// <summary>
    /// Gets the error code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets optional additional context about the error.
    /// </summary>
    public string? Context { get; }

    private Error(string code, string message, string? context = null)
    {
        Code = code;
        Message = message;
        Context = context;
    }

    /// <summary>
    /// Creates a general error with the specified message.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <returns>An error instance</returns>
    public static Error Create(string message) => new("GENERAL_ERROR", message);

    /// <summary>
    /// Creates an error with the specified code and message.
    /// </summary>
    /// <param name="code">The error code</param>
    /// <param name="message">The error message</param>
    /// <param name="context">Optional additional context</param>
    /// <returns>An error instance</returns>
    public static Error Create(string code, string message, string? context = null) => new(code, message, context);

    /// <summary>
    /// Creates a validation error.
    /// </summary>
    /// <param name="message">The validation error message</param>
    /// <param name="field">The field that failed validation</param>
    /// <returns>A validation error instance</returns>
    public static Error Validation(string message, string? field = null) => 
        new("VALIDATION_ERROR", message, field);

    /// <summary>
    /// Creates a parsing error.
    /// </summary>
    /// <param name="message">The parsing error message</param>
    /// <param name="position">The position where parsing failed</param>
    /// <returns>A parsing error instance</returns>
    public static Error Parsing(string message, string? position = null) => 
        new("PARSING_ERROR", message, position);

    /// <summary>
    /// Creates a configuration error.
    /// </summary>
    /// <param name="message">The configuration error message</param>
    /// <param name="configKey">The configuration key that caused the error</param>
    /// <returns>A configuration error instance</returns>
    public static Error Configuration(string message, string? configKey = null) => 
        new("CONFIGURATION_ERROR", message, configKey);

    /// <summary>
    /// Returns a string representation of the error.
    /// </summary>
    /// <returns>A formatted error string</returns>
    public override string ToString() => 
        Context != null ? $"[{Code}] {Message} (Context: {Context})" : $"[{Code}] {Message}";
}