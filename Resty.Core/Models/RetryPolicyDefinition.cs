namespace Resty.Core.Models;

/// <summary>
/// Retry behavior for one HTTP test.
/// </summary>
public sealed record RetryPolicyDefinition
{
  private static readonly int[] DefaultRetryStatuses = [408, 429, 500, 502, 503, 504];

  /// <summary>
  /// Total number of request attempts, including the first attempt.
  /// </summary>
  public int Attempts { get; init; } = 1;

  /// <summary>
  /// Base delay between attempts. Expressions resolve at test execution time.
  /// </summary>
  public object? Delay { get; init; }

  /// <summary>
  /// Delay progression used between attempts.
  /// </summary>
  public RetryBackoffKind Backoff { get; init; } = RetryBackoffKind.Fixed;

  /// <summary>
  /// HTTP response status codes that can trigger another attempt.
  /// </summary>
  public ExpectedStatusCodes Statuses { get; init; } = new(DefaultRetryStatuses);

  /// <summary>
  /// Whether failed header and JSON value expectations can trigger another attempt.
  /// </summary>
  public bool ExpectationFailures { get; init; }

  /// <summary>
  /// Creates the historical scalar retry behavior where the scalar is a retry
  /// count and delays use capped exponential backoff beginning at one second.
  /// </summary>
  public static RetryPolicyDefinition FromLegacyRetryCount( int retryCount )
  {
    return new RetryPolicyDefinition {
      Attempts = Math.Max(1, retryCount + 1),
      Delay = TimeSpan.FromSeconds(1),
      Backoff = RetryBackoffKind.Exponential,
      Statuses = new ExpectedStatusCodes(DefaultRetryStatuses)
    };
  }

  public static implicit operator RetryPolicyDefinition( int retryCount )
  {
    return FromLegacyRetryCount(retryCount);
  }
}

/// <summary>
/// Supported delay progressions between retry attempts.
/// </summary>
public enum RetryBackoffKind
{
  Fixed,
  Exponential
}
