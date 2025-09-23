namespace Resty.Core.Execution;

using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resty.Core.Models;
using Resty.Core.Variables;

/// <summary>
/// Executes HTTP tests with variable resolution and response capture.
/// </summary>
public class HttpTestExecutor
{
  private readonly HttpClient _httpClient;

  public HttpTestExecutor( HttpClient httpClient )
  {
    _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
  }

  /// <summary>
  /// Executes an HTTP test with variable resolution and response capture.
  /// </summary>
  /// <param name="test">The test to execute.</param>
  /// <param name="variableStore">Variable store for resolution and capture.</param>
  /// <param name="retryCount">Number of times to retry on failure (0 means no retries).</param>
  /// <param name="optTimeout">Global timeout override (from CLI --timeout).</param>
  /// <param name="optTimeoutWasSet">Whether the global timeout was explicitly set by user.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>Test result with timing and response data.</returns>
  public async Task<TestResult> ExecuteTestAsync(
      HttpTest test,
      VariableStore variableStore,
      int retryCount = 0,
      int? optTimeout = null,
      bool optTimeoutWasSet = false,
      CancellationToken cancellationToken = default )
  {
    return await ExecuteWithRetryAsync(test, variableStore, retryCount, optTimeout, optTimeoutWasSet, cancellationToken);
  }

  /// <summary>
  /// Executes an HTTP test with retry logic and exponential backoff.
  /// </summary>
  private async Task<TestResult> ExecuteWithRetryAsync(
      HttpTest test,
      VariableStore variableStore,
      int retryCount,
      int? optTimeout,
      bool optTimeoutWasSet,
      CancellationToken cancellationToken )
  {
    var totalAttempts = Math.Max(1, retryCount + 1); // At least 1 attempt
    Exception? lastException = null;
    TestResult? lastResult = null;

    // Determine timeout to use based on priority:
    // 1. OptTimeout (if OptTimeoutWasSet) overrides everything
    // 2. Test-level timeout overrides code-default
    // 3. Code-default timeout (HttpClient default)
    var timeoutSeconds = DetermineTimeoutSeconds(test, optTimeout, optTimeoutWasSet);

    for (var attempt = 1; attempt <= totalAttempts; attempt++) {
      var startTime = DateTime.UtcNow;
      var isLastAttempt = attempt == totalAttempts;

      try {
        // Step 1: Capture variable snapshot for debugging
        var variableSnapshot = variableStore.GetVariableSnapshot();

        // Step 2: Apply variable resolution
        var resolvedTest = ResolveVariables(test, variableStore);

        // Step 3: Create HTTP request
        var request = CreateHttpRequest(resolvedTest);

        // Step 4: Execute HTTP request with timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutSeconds.HasValue) {
          timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds.Value));
        }

        var response = await _httpClient.SendAsync(request, timeoutCts.Token);

        // Step 5: Process response
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var responseHeaders = ExtractResponseHeaders(response);

        var endTime = DateTime.UtcNow;

        // Step 6: Determine pass/fail based on expectations
        var isPass = IsPass(response, test);

        // Step 7: Extract variables from response if test considered passed
        var extractedVariables = new Dictionary<string, object>();
        var captureFailures = new List<string>();
        var parseFailed = false;
        if (isPass && resolvedTest.Extractors.Count > 0) {
          var extraction = ExtractResponseVariables(responseBody, resolvedTest.Extractors);
          extractedVariables = extraction.Extracted;
          captureFailures = extraction.Failures;
          parseFailed = extraction.ParseFailed;
        }

        // Step 7.1: Enforce strict capture rules for 2xx (except 201)
        if (isPass && resolvedTest.Extractors.Count > 0 && ShouldTreatCaptureStrict(response.StatusCode)) {
          var missingKeys = resolvedTest.Extractors.Keys
            .Where(k => !extractedVariables.ContainsKey(k))
            .Distinct()
            .ToList();

          if (missingKeys.Count > 0 || captureFailures.Count > 0 || parseFailed) {
            var details = new List<string>();
            if (missingKeys.Count > 0) { details.Add($"missing: {string.Join(", ", missingKeys)}"); }
            if (captureFailures.Count > 0) { details.Add($"errors: {string.Join(", ", captureFailures)}"); }
            if (parseFailed) { details.Add("response not JSON or empty"); }
            var captureErrorMessage = "Capture failed (" + string.Join("; ", details) + ")";

            var failureResult = TestResult.Failure(
              test,
              startTime,
              endTime,
              captureErrorMessage,
              null,
              response.StatusCode,
              responseBody,
              CreateRequestInfo(resolvedTest),
              variableSnapshot
            );

            return failureResult;
          }
        }

        // Step 8: Create result
        if (isPass) {
          var result = TestResult.Success(
            test,
            startTime,
            endTime,
            response.StatusCode,
            responseHeaders,
            responseBody,
            extractedVariables,
            CreateRequestInfo(resolvedTest),
            variableSnapshot
          );

          // Add retry information if there were previous attempts
          if (attempt > 1) {
            result = result with {
              ErrorMessage = $"Succeeded on attempt {attempt}/{totalAttempts}"
            };
          }

          return result;
        } else {
          var errorMessage = BuildFailureMessage(response, test, attempt, totalAttempts);

          lastResult = TestResult.Failure(
            test,
            startTime,
            endTime,
            errorMessage,
            null,
            response.StatusCode,
            responseBody,
            CreateRequestInfo(resolvedTest),
            variableSnapshot
          );

          // If this is the last attempt or it's a non-retryable status, return immediately
          if (isLastAttempt || !IsRetryableStatusCode(response.StatusCode)) {
            return lastResult;
          }
        }
      } catch (Exception ex) {
        var endTime = DateTime.UtcNow;
        var variableSnapshot = variableStore.GetVariableSnapshot();

        var errorMessage = $"Request failed: {ex.Message}";
        if (attempt > 1) {
          errorMessage += $" (attempt {attempt}/{totalAttempts})";
        }

        lastException = ex;
        lastResult = TestResult.Failure(test, startTime, endTime, errorMessage, ex, variableSnapshot: variableSnapshot);

        // If this is the last attempt or it's a non-retryable exception, return immediately
        if (isLastAttempt || !IsRetryableException(ex)) {
          return lastResult;
        }
      }

      // Wait before retry (exponential backoff: 1s, 2s, 4s, 8s, ...)
      if (!isLastAttempt) {
        var delayMs = (int)Math.Pow(2, attempt - 1) * 1000; // 1s, 2s, 4s, 8s
        await Task.Delay(Math.Min(delayMs, 30000), cancellationToken); // Cap at 30 seconds
      }
    }

    // This shouldn't happen, but return the last result as fallback
    return lastResult ?? TestResult.Failure(test, DateTime.UtcNow, DateTime.UtcNow,
      "Unknown error during retry execution", lastException);
  }

  private static bool IsPass( HttpResponseMessage response, HttpTest test )
  {
    return test.ExpectedStatus.HasValue
      ? (int)response.StatusCode == test.ExpectedStatus.Value
      : response.IsSuccessStatusCode;
  }

  private static string BuildFailureMessage( HttpResponseMessage response, HttpTest test, int attempt, int totalAttempts )
  {
    string errorMessage;

    if (test.ExpectedStatus.HasValue) {
      errorMessage = $"Expected status {test.ExpectedStatus.Value} but got {(int)response.StatusCode} {response.ReasonPhrase}";
    } else {
      errorMessage = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
    }

    if (attempt > 1) {
      errorMessage += $" (attempt {attempt}/{totalAttempts})";
    }

    return errorMessage;
  }

  private static bool ShouldTreatCaptureStrict( HttpStatusCode statusCode )
  {
    // Strict capture for 2xx except 204 (No Content)
    var code = (int)statusCode;
    return code >= 200 && code <= 299 && statusCode != HttpStatusCode.NoContent;
  }

  private static bool IsRetryableStatusCode( HttpStatusCode statusCode )
  {
    // Retry on server errors and some client errors
    return statusCode switch {
      HttpStatusCode.InternalServerError => true,    // 500
      HttpStatusCode.BadGateway => true,             // 502
      HttpStatusCode.ServiceUnavailable => true,     // 503
      HttpStatusCode.GatewayTimeout => true,         // 504
      HttpStatusCode.RequestTimeout => true,         // 408
      HttpStatusCode.TooManyRequests => true,        // 429
      _ => false
    };
  }

  /// <summary>
  /// Determines if an exception is retryable.
  /// </summary>
  private static bool IsRetryableException( Exception ex )
  {
    // Retry on network-related exceptions
    return ex switch {
      HttpRequestException => true,
      TaskCanceledException when ex.InnerException is TimeoutException => true,
      SocketException => true,
      _ => false
    };
  }

  /// <summary>
  /// Determines the timeout to use for HTTP requests based on priority:
  /// 1. OptTimeout (if OptTimeoutWasSet) overrides everything
  /// 2. Test-level timeout overrides code-default
  /// 3. Code-default timeout (HttpClient default) - returns null to use HttpClient's timeout
  /// </summary>
  private static int? DetermineTimeoutSeconds( HttpTest test, int? optTimeout, bool optTimeoutWasSet )
  {
    // Priority 1: CLI option timeout (if explicitly set)
    if (optTimeoutWasSet && optTimeout.HasValue) {
      return optTimeout.Value;
    }

    // Priority 2: Test-level timeout
    if (test.Timeout.HasValue) {
      return test.Timeout.Value;
    }

    // Priority 3: Use HttpClient's default timeout (don't override)
    return null;
  }

  /// <summary>
  /// Resolves variables in the test definition.
  /// </summary>
  private ResolvedHttpTest ResolveVariables( HttpTest test, VariableStore variableStore )
  {
    return new ResolvedHttpTest {
      Name = test.Name,
      Method = test.Method,
      Url = variableStore.ResolveVariables(test.Url),
      ContentType = variableStore.ResolveVariables(test.ContentType),
      Authorization = !string.IsNullOrEmpty(test.Authorization)
        ? variableStore.ResolveVariables(test.Authorization)
        : null,
      Headers = test.Headers.ToDictionary(
          kvp => kvp.Key,
          kvp => variableStore.ResolveVariables(kvp.Value)
        ),
      Body = !string.IsNullOrEmpty(test.Body)
        ? variableStore.ResolveVariables(test.Body)
        : null,
      Extractors = test.Extractors
    };
  }

  /// <summary>
  /// Creates an HTTP request from the resolved test.
  /// </summary>
  private HttpRequestMessage CreateHttpRequest( ResolvedHttpTest resolvedTest )
  {
    var method = new HttpMethod(resolvedTest.Method);
    var request = new HttpRequestMessage(method, resolvedTest.Url);

    var contentType = resolvedTest.ContentType ?? "application/json";
    var authorization = resolvedTest.Authorization;

    // Set the content if resolvedTest.Body is present.
    if (!string.IsNullOrEmpty(resolvedTest.Body)) {
      request.Content = new StringContent(
        resolvedTest.Body,
        Encoding.UTF8,
        contentType
      );
    }

    // Add the 'Content-Type' header.
    if (!string.IsNullOrEmpty(contentType)) {
      if (request.Content != null) {
        try {
          request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        } catch {
          // Fallback: add without validation if parsing fails
          request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }
      }
    }

    // Add the 'Authorization' header.
    if (!string.IsNullOrEmpty(authorization)) {
      var parts = authorization.Split(' ', 2);
      if (parts.Length == 2) {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(parts[0], parts[1]);
      } else {
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
      }
    }

    // Set remaining headers, excluding 'Content-Type' and 'Authorization' which were handled above.
    foreach (var (key, value) in resolvedTest.Headers) {
      if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase)
          || string.Equals(key, "Authorization", StringComparison.OrdinalIgnoreCase)) {
        continue; // Already handled.
      }
      request.Headers.TryAddWithoutValidation(key, value);
    }

    return request;
  }

  /// <summary>
  /// Extracts response headers as a string dictionary.
  /// </summary>
  private Dictionary<string, string> ExtractResponseHeaders( HttpResponseMessage response )
  {
    var headers = new Dictionary<string, string>();

    // Response headers
    foreach (var header in response.Headers) {
      headers[header.Key] = string.Join(", ", header.Value);
    }

    // Content headers
    if (response.Content != null) {
      foreach (var header in response.Content.Headers) {
        headers[header.Key] = string.Join(", ", header.Value);
      }
    }

    return headers;
  }

  /// <summary>
  /// Extracts variables from the response body using the configured extractors.
  /// </summary>
  private (Dictionary<string, object> Extracted, List<string> Failures, bool ParseFailed) ExtractResponseVariables(
      string responseBody,
      Dictionary<string, string> extractors )
  {
    var extractedVariables = new Dictionary<string, object>();
    var failures = new List<string>();
    var parseFailed = false;

    if (string.IsNullOrEmpty(responseBody)) {
      // No body to parse; treat as parse failed for strict evaluation
      return (extractedVariables, failures, true);
    }

    try {
      // Parse response as JSON
      var jsonResponse = JToken.Parse(responseBody);

      foreach (var (variableName, jsonPath) in extractors) {
        try {
          // Use JSON path to extract value
          var extractedToken = jsonResponse.SelectToken(jsonPath);
          if (extractedToken != null) {
            // Convert JToken to appropriate .NET type
            var extractedValue = ConvertJTokenToObject(extractedToken);
            extractedVariables[variableName] = extractedValue;
          } else {
            failures.Add(variableName);
          }
        } catch (Exception ex) {
          // Log extraction failure but don't fail the test unless strict rules apply
          Console.WriteLine($"Failed to extract variable '{variableName}' using path '{jsonPath}': {ex.Message}");
          failures.Add(variableName);
        }
      }
    } catch (JsonException) {
      // Response is not valid JSON - can't extract variables
      parseFailed = true;
    }

    return (extractedVariables, failures, parseFailed);
  }

  /// <summary>
  /// Converts a JToken to an appropriate .NET object type.
  /// </summary>
  private object ConvertJTokenToObject( JToken token )
  {
    return token.Type switch {
      JTokenType.String => token.Value<string>() ?? string.Empty,
      JTokenType.Integer => token.Value<long>(),
      JTokenType.Float => token.Value<double>(),
      JTokenType.Boolean => token.Value<bool>(),
      JTokenType.Date => token.Value<DateTime>(),
      JTokenType.Null => null!,
      JTokenType.Array => token.ToObject<object[]>() ?? Array.Empty<object>(),
      JTokenType.Object => token.ToObject<Dictionary<string, object>>() ?? new Dictionary<string, object>(),
      _ => token.ToString()
    };
  }

  /// <summary>
  /// Creates request info for the test result.
  /// </summary>
  private HttpRequestInfo CreateRequestInfo( ResolvedHttpTest resolvedTest )
  {
    return new HttpRequestInfo {
      Method = resolvedTest.Method,
      Url = resolvedTest.Url,
      Headers = resolvedTest.Headers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
      Body = resolvedTest.Body,
      ContentType = resolvedTest.ContentType
    };
  }

  /// <summary>
  /// Internal representation of a test with all variables resolved.
  /// </summary>
  private record ResolvedHttpTest
  {
    public string Name { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/json";
    public string? Authorization { get; init; }
    public Dictionary<string, string> Headers { get; init; } = new();
    public string? Body { get; init; }
    public Dictionary<string, string> Extractors { get; init; } = new();
  }
}
