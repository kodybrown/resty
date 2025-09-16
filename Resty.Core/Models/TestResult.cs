namespace Resty.Core.Models;

using System.Net;

/// <summary>
/// Represents the result of executing an HTTP test.
/// </summary>
public record TestResult
{
  /// <summary>
  /// The test that was executed.
  /// </summary>
  public HttpTest Test { get; init; } = null!;

  /// <summary>
  /// Execution status of the test.
  /// </summary>
  public TestStatus Status { get; init; } = TestStatus.NotRun;

  /// <summary>
  /// Time when test execution started.
  /// </summary>
  public DateTime StartTime { get; init; }

  /// <summary>
  /// Time when test execution completed.
  /// </summary>
  public DateTime EndTime { get; init; }

  /// <summary>
  /// Total execution duration.
  /// </summary>
  public TimeSpan Duration => EndTime - StartTime;

  /// <summary>
  /// HTTP status code received (if request was sent).
  /// </summary>
  public HttpStatusCode? StatusCode { get; init; }

  /// <summary>
  /// Response headers received.
  /// </summary>
  public Dictionary<string, string> ResponseHeaders { get; init; } = new();

  /// <summary>
  /// Raw response body content.
  /// </summary>
  public string? ResponseBody { get; init; }

  /// <summary>
  /// Variables extracted from the response using success extractors.
  /// </summary>
  public Dictionary<string, object> ExtractedVariables { get; init; } = new();

  /// <summary>
  /// Error message if the test failed.
  /// </summary>
  public string? ErrorMessage { get; init; }

  /// <summary>
  /// Exception details if the test failed due to an exception.
  /// </summary>
  public Exception? Exception { get; init; }

  /// <summary>
  /// Actual HTTP request that was sent (with variables resolved).
  /// </summary>
  public HttpRequestInfo? RequestInfo { get; init; }

  /// <summary>
  /// Snapshot of all available variables at the time of test execution.
  /// Includes variable name, value, and source (Included, File, Captured).
  /// </summary>
  public Dictionary<string, (object Value, string Source)> VariableSnapshot { get; init; } = new();

  /// <summary>
  /// Determines if this test passed.
  /// </summary>
  public bool Passed => Status == TestStatus.Passed;

  /// <summary>
  /// Determines if this test failed.
  /// </summary>
  public bool Failed => Status == TestStatus.Failed;

  /// <summary>
  /// Creates a successful test result.
  /// </summary>
  public static TestResult Success(
      HttpTest test,
      DateTime startTime,
      DateTime endTime,
      HttpStatusCode statusCode,
      Dictionary<string, string> responseHeaders,
      string? responseBody,
      Dictionary<string, object> extractedVariables,
      HttpRequestInfo requestInfo,
      Dictionary<string, (object Value, string Source)>? variableSnapshot = null )
  {
    return new TestResult {
      Test = test,
      Status = TestStatus.Passed,
      StartTime = startTime,
      EndTime = endTime,
      StatusCode = statusCode,
      ResponseHeaders = responseHeaders,
      ResponseBody = responseBody,
      ExtractedVariables = extractedVariables,
      RequestInfo = requestInfo,
      VariableSnapshot = variableSnapshot ?? new()
    };
  }

  /// <summary>
  /// Creates a skipped test result.
  /// </summary>
  public static TestResult Skipped(
      HttpTest test,
      string reason,
      Dictionary<string, (object Value, string Source)>? variableSnapshot = null )
  {
    var now = DateTime.UtcNow;
    return new TestResult {
      Test = test,
      Status = TestStatus.Skipped,
      StartTime = now,
      EndTime = now,
      ErrorMessage = reason,
      VariableSnapshot = variableSnapshot ?? new()
    };
  }

  /// <summary>
  /// Creates a failed test result.
  /// </summary>
  public static TestResult Failure(
      HttpTest test,
      DateTime startTime,
      DateTime endTime,
      string errorMessage,
      Exception? exception = null,
      HttpStatusCode? statusCode = null,
      string? responseBody = null,
      HttpRequestInfo? requestInfo = null,
      Dictionary<string, (object Value, string Source)>? variableSnapshot = null )
  {
    return new TestResult {
      Test = test,
      Status = TestStatus.Failed,
      StartTime = startTime,
      EndTime = endTime,
      ErrorMessage = errorMessage,
      Exception = exception,
      StatusCode = statusCode,
      ResponseBody = responseBody,
      RequestInfo = requestInfo,
      VariableSnapshot = variableSnapshot ?? new()
    };
  }
}

/// <summary>
/// Test execution status.
/// </summary>
public enum TestStatus
{
  NotRun,
  Running,
  Passed,
  Failed,
  Skipped
}

/// <summary>
/// Information about the HTTP request that was sent.
/// </summary>
public record HttpRequestInfo
{
  /// <summary>
  /// HTTP method used.
  /// </summary>
  public string Method { get; init; } = string.Empty;

  /// <summary>
  /// Final URL after variable resolution.
  /// </summary>
  public string Url { get; init; } = string.Empty;

  /// <summary>
  /// Headers that were sent (after variable resolution).
  /// </summary>
  public Dictionary<string, string> Headers { get; init; } = new();

  /// <summary>
  /// Request body that was sent (after variable resolution).
  /// </summary>
  public string? Body { get; init; }

  /// <summary>
  /// Content-Type header that was used.
  /// </summary>
  public string ContentType { get; init; } = string.Empty;
}
