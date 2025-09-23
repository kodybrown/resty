namespace Resty.Core.Execution;

using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resty.Core.Models;
using Resty.Core.Variables;
using System.Collections;

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

        // Step 6.1: If status passed and header expectations exist, validate expected headers
        if (isPass && test.Expect?.Headers is { Count: > 0 }) {
          var headerValidation = ValidateExpectedHeaders(responseHeaders, test.Expect.Headers, variableStore);
          if (!headerValidation.IsValid) {
            var failureResult = TestResult.Failure(
              test,
              startTime,
              endTime,
              headerValidation.ErrorMessage,
              null,
              response.StatusCode,
              responseBody,
              CreateRequestInfo(resolvedTest),
              variableSnapshot
            );
            return failureResult;
          }
        }

        // Step 6.2: If status and headers passed, validate JSON value expectations
        var valueExpectCaptured = new Dictionary<string, object>();
        if (isPass && test.Expect?.Values is { Count: > 0 }) {
          var valueValidation = ValidateExpectedValues(responseBody, test.Expect.Values, variableStore);
          if (!valueValidation.IsValid) {
            var failureResult = TestResult.Failure(
              test,
              startTime,
              endTime,
              valueValidation.ErrorMessage,
              null,
              response.StatusCode,
              responseBody,
              CreateRequestInfo(resolvedTest),
              variableSnapshot
            );
            return failureResult;
          }
          valueExpectCaptured = valueValidation.StoredVariables;
        }

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
          // Merge stored variables from value expectations (without overriding capture keys)
          foreach (var (k, v) in valueExpectCaptured) {
            if (!extractedVariables.ContainsKey(k)) {
              extractedVariables[k] = v;
            }
          }

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
    var resolvedUrl = variableStore.ResolveVariables(test.Url);
    var resolvedContentType = variableStore.ResolveVariables(test.ContentType);

    // Resolve headers (values only)
    var resolvedHeaders = test.Headers.ToDictionary(
      kvp => kvp.Key,
      kvp => variableStore.ResolveVariables(kvp.Value)
    );

    // Resolve authorization
    var resolvedAuth = !string.IsNullOrEmpty(test.Authorization)
      ? variableStore.ResolveVariables(test.Authorization)
      : null;

    // Resolve/serialize body
    string? resolvedBody = null;
    if (test.RawBody is null) {
      // Fallback for legacy string Body when RawBody is not provided
      resolvedBody = !string.IsNullOrEmpty(test.Body)
        ? variableStore.ResolveVariables(test.Body)
        : null;
    } else if (test.RawBody is string s) {
      resolvedBody = variableStore.ResolveVariables(s);
    } else {
      // Structured body: dictionary/array
      var normalized = ResolveStructuredVariablesDeep(test.RawBody, variableStore);
      if (string.IsNullOrEmpty(resolvedContentType) || resolvedContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) {
        resolvedBody = JsonConvert.SerializeObject(normalized);
        if (string.IsNullOrEmpty(resolvedContentType)) {
          resolvedContentType = "application/json";
        }
      } else if (resolvedContentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) {
        if (normalized is IDictionary<string, object?> mapDict) {
          resolvedBody = SerializeFormUrlEncoded(mapDict);
        } else {
          throw new InvalidOperationException("Structured body for application/x-www-form-urlencoded must be a mapping (dictionary).");
        }
      } else {
        throw new InvalidOperationException($"Structured body is only supported with content types 'application/json' or 'application/x-www-form-urlencoded'. Got '{resolvedContentType}'.");
      }
    }

    return new ResolvedHttpTest {
      Name = test.Name,
      Method = test.Method,
      Url = resolvedUrl,
      ContentType = resolvedContentType,
      Authorization = resolvedAuth,
      Headers = resolvedHeaders,
      Body = resolvedBody,
      Extractors = test.Extractors
    };
  }

  private static IDictionary<string, object?> ToStringObjectDictionary( object obj )
  {
    if (obj is IDictionary<string, object?> stringDict) { return stringDict; }
    if (obj is IDictionary<object, object?> objDict) {
      var result = new Dictionary<string, object?>();
      foreach (var kvp in objDict) {
        var key = kvp.Key?.ToString() ?? string.Empty;
        result[key] = kvp.Value;
      }
      return result;
    }
    throw new InvalidOperationException("Expected a dictionary for form-encoded body.");
  }

  private static string SerializeFormUrlEncoded( IDictionary<string, object?> dict )
  {
    var parts = new List<string>();
    foreach (var (k, v) in dict) {
      var key = Uri.EscapeDataString(k ?? string.Empty);
      var val = Uri.EscapeDataString(v?.ToString() ?? string.Empty);
      parts.Add($"{key}={val}");
    }
    return string.Join("&", parts);
  }

  private static object ResolveStructuredVariablesDeep( object value, VariableStore vars )
  {
    switch (value) {
      case string s:
        return vars.ResolveVariables(s);
      case IDictionary<string, object?> stringDict:
        return stringDict.ToDictionary(kvp => kvp.Key, kvp => ResolveStructuredVariablesDeep(kvp.Value!, vars));
      case IDictionary<object, object?> objDict:
        var result = new Dictionary<string, object?>();
        foreach (var kvp in objDict) {
          var key = kvp.Key?.ToString() ?? string.Empty;
          result[key] = ResolveStructuredVariablesDeep(kvp.Value!, vars);
        }
        return result;
      case IEnumerable<object?> list:
        return list.Select(item => item is null ? null : ResolveStructuredVariablesDeep(item, vars)).ToList();
      default:
        return value; // numbers, bools, etc.
    }
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

  private (bool IsValid, string ErrorMessage, Dictionary<string, object> StoredVariables) ValidateExpectedValues(
      string? responseBody,
      List<ValueExpectation> expectations,
      VariableStore variableStore )
  {
    var stored = new Dictionary<string, object>();
    if (expectations == null || expectations.Count == 0) {
      return (true, string.Empty, stored);
    }

    if (string.IsNullOrEmpty(responseBody)) {
      return (false, "Expected values mismatch: response body is empty or missing", stored);
    }

    JToken json;
    try {
      json = JToken.Parse(responseBody);
    } catch (JsonException) {
      return (false, "Expected values mismatch: response body is not valid JSON", stored);
    }

    var issues = new List<string>();

    foreach (var rule in expectations) {
      var key = rule.Key ?? string.Empty;
      var op = NormalizeOp(rule.Op ?? string.Empty);
      var ignoreCase = rule.IgnoreCase ?? true; // default true

      // exists / not_exists don't need a value
      if (IsExistenceOp(op)) {
        var tokens = SelectTokensSafe(json, key);
        var exists = tokens.Any();
        var passed = op == "exists" ? exists : !exists;
        if (!passed) {
          issues.Add(op == "exists" ? $"missing: {key}" : $"should not exist: {key}");
        } else if (exists && !string.IsNullOrWhiteSpace(rule.StoreAs)) {
          var first = tokens.First();
          stored[rule.StoreAs] = ConvertJTokenToObject(first);
        }
        continue;
      }

      // Resolve expected value (with variables and keywords)
      var (expValue, expIsNull, expIsEmpty) = ResolveExpectedValue(rule.Value, variableStore);

      var matchesAny = false;
      var tokensToCheck = SelectTokensSafe(json, key).ToList();
      if (!tokensToCheck.Any()) {
        issues.Add($"missing: {key}");
        continue;
      }

      foreach (var token in tokensToCheck) {
        if (EvaluateComparison(token, op, expValue, expIsNull, expIsEmpty, ignoreCase)) {
          matchesAny = true;
          if (!string.IsNullOrWhiteSpace(rule.StoreAs)) {
            stored[rule.StoreAs] = ConvertJTokenToObject(token);
          }
          break;
        }
      }

      if (!matchesAny) {
        var gotSample = tokensToCheck.FirstOrDefault();
        var gotStr = gotSample != null ? tokenToPreview(gotSample) : "<missing>";
        issues.Add($"{key} expected {op} '{ToDisplayString(expValue, expIsNull, expIsEmpty)}' but got {gotStr}");
      }
    }

    if (issues.Count > 0) {
      return (false, "Expected values mismatch: " + string.Join("; ", issues), stored);
    }

    return (true, string.Empty, stored);

    static string tokenToPreview( JToken tok )
    {
      if (tok == null) {
        return "<null>";
      }
      return tok.Type switch {
        JTokenType.String => tok.Value<string>() ?? string.Empty,
        JTokenType.Null => "<null>",
        _ => tok.ToString(Newtonsoft.Json.Formatting.None)
      };
    }
  }

  private static IEnumerable<JToken> SelectTokensSafe( JToken json, string jsonPath )
  {
    try {
      return json.SelectTokens(jsonPath) ?? Enumerable.Empty<JToken>();
    } catch {
      // Fallback to single token selection to handle bad paths
      var t = json.SelectToken(jsonPath);
      return t != null ? new[] { t } : Enumerable.Empty<JToken>();
    }
  }

  private static (object? Value, bool IsNull, bool IsEmpty) ResolveExpectedValue(object? raw, VariableStore vars)
  {
    if (raw == null) return (null, false, false);

    if (raw is string s) {
      // Handle keyword literals before variable resolution
      if (string.Equals(s, "$null", StringComparison.OrdinalIgnoreCase)) {
        return (null, true, false);
      }
      if (string.Equals(s, "$empty", StringComparison.OrdinalIgnoreCase)) {
        return (string.Empty, false, true);
      }

      // Resolve variables within string (may produce numbers/strings)
      var resolved = vars.ResolveVariables(s);
      if (string.Equals(resolved, "$null", StringComparison.OrdinalIgnoreCase)) {
        return (null, true, false);
      }
      if (string.Equals(resolved, "$empty", StringComparison.OrdinalIgnoreCase)) {
        return (string.Empty, false, true);
      }
      return (resolved, false, false);
    }
    return (raw, false, false);
  }

  private static bool IsExistenceOp( string op )
  {
    return string.Equals(op, "exists", StringComparison.OrdinalIgnoreCase)
        || string.Equals(op, "not_exists", StringComparison.OrdinalIgnoreCase);
  }

  private static string NormalizeOp( string op )
  {
    var o = op.Trim().ToLowerInvariant().Replace(" ", "");
    return o switch {
      "eq" => "equals",
      "ne" => "not_equals",
      "gt" => "greater_than",
      "gte" => "greater_than_or_equal",
      "lt" => "less_than",
      "lte" => "less_than_or_equal",
      "starts_with" => "startswith",
      "ends_with" => "endswith",
      _ => o
    };
  }

  private static bool EvaluateComparison(
      JToken token,
      string op,
      object? expected,
      bool expIsNull,
      bool expIsEmpty,
      bool ignoreCase )
  {
    // Handle $null / $empty on equals and not_equals quickly
    if (op is "equals" or "not_equals") {
      var isNull = token.Type == JTokenType.Null;
      var tokVal = isNull ? null : ConvertJTokenToObject(token);
      var left = tokVal?.ToString() ?? string.Empty;
      var right = expected?.ToString() ?? string.Empty;
      var equal = expIsNull ? isNull : (expIsEmpty ? (left == string.Empty) : StringEquals(left, right, ignoreCase));
      return op == "equals" ? equal : !equal;
    }

    // exists/not_exists should have been handled earlier
    if (op is "exists" or "not_exists") {
      return true;
    }

    // String operations require strings
    if (op.StartsWith("starts") || op.StartsWith("ends") || op.Contains("contains")) {
      var left = token.Type == JTokenType.Null ? string.Empty : token.ToString();
      var right = expected?.ToString() ?? string.Empty;
      var comp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

      bool result = op switch {
        "contains" => left?.IndexOf(right, comp) >= 0,
        "not_contains" => left?.IndexOf(right, comp) < 0,
        "startswith" => left?.StartsWith(right, comp) == true,
        "not_startswith" => left?.StartsWith(right, comp) != true,
        "endswith" => left?.EndsWith(right, comp) == true,
        "not_endswith" => left?.EndsWith(right, comp) != true,
        _ => false
      };
      return result;
    }

    // Relational: numeric or date
    // Try direct date parse from string token and string expected
    if (token.Type == JTokenType.String && expected is string es &&
        DateTimeOffset.TryParse(token.Value<string>(), System.Globalization.CultureInfo.InvariantCulture,
          System.Globalization.DateTimeStyles.RoundtripKind | System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dLeft) &&
        DateTimeOffset.TryParse(es, System.Globalization.CultureInfo.InvariantCulture,
          System.Globalization.DateTimeStyles.RoundtripKind | System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dRight)) {
      return op switch {
        "greater_than" => dLeft > dRight,
        "greater_than_or_equal" => dLeft >= dRight,
        "less_than" => dLeft < dRight,
        "less_than_or_equal" => dLeft <= dRight,
        _ => false
      };
    }

    // Try numeric first
    if (TryGetNumeric(token, out var numLeft) && TryCoerceToDouble(expected, out var numRight)) {
      return op switch {
        "greater_than" => numLeft > numRight,
        "greater_than_or_equal" => numLeft >= numRight,
        "less_than" => numLeft < numRight,
        "less_than_or_equal" => numLeft <= numRight,
        _ => false
      };
    }

    // Try date
    if (TryGetDate(token, out var dateLeft) && TryCoerceToDate(expected, out var dateRight)) {
      return op switch {
        "greater_than" => dateLeft > dateRight,
        "greater_than_or_equal" => dateLeft >= dateRight,
        "less_than" => dateLeft < dateRight,
        "less_than_or_equal" => dateLeft <= dateRight,
        _ => false
      };
    }

    // Fallback type mismatch → only equals/not_equals treat strings; others fail
    return false;
  }

  private static bool StringEquals( string a, string b, bool ignoreCase )
  {
    return string.Equals(a ?? string.Empty, b ?? string.Empty, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
  }

  private static bool TryGetNumeric( JToken token, out double value )
  {
    value = default;
    switch (token.Type) {
      case JTokenType.Integer:
        value = token.Value<long>();
        return true;
      case JTokenType.Float:
        value = token.Value<double>();
        return true;
      case JTokenType.String:
        return double.TryParse(token.Value<string>(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value);
      default:
        return false;
    }
  }

  private static bool TryCoerceToDouble( object? expected, out double value )
  {
    value = default;
    if (expected is null) {
      return false;
    }
    if (expected is double d) { value = d; return true; }
    if (expected is float f) { value = f; return true; }
    if (expected is long l) { value = l; return true; }
    if (expected is int i) { value = i; return true; }
    if (expected is string s) {
      return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value);
    }
    return false;
  }

  private static bool TryGetDate(JToken token, out DateTimeOffset value)
  {
    value = default;
    if (token.Type == JTokenType.Date) { value = token.Value<DateTimeOffset>(); return true; }
    if (token.Type == JTokenType.String) {
      var s = token.Value<string>();
      var styles = System.Globalization.DateTimeStyles.RoundtripKind | System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal;
      if (DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, styles, out var dto)) { value = dto; return true; }
    }
    return false;
  }

  private static bool TryCoerceToDate(object? expected, out DateTimeOffset value)
  {
    value = default;
    if (expected is DateTime dt) { value = new DateTimeOffset(dt); return true; }
    if (expected is DateTimeOffset dto) { value = dto; return true; }
    if (expected is string s) {
      var styles = System.Globalization.DateTimeStyles.RoundtripKind | System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal;
      if (DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, styles, out var dto2)) { value = dto2; return true; }
    }
    return false;
  }

  private (bool IsValid, string ErrorMessage) ValidateExpectedHeaders(
      Dictionary<string, string> responseHeaders,
      Dictionary<string, string> expectedHeaders,
      VariableStore variableStore )
  {
    if (expectedHeaders == null || expectedHeaders.Count == 0) {
      return (true, string.Empty);
    }

    // Build a case-insensitive map of response headers
    var actual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var kvp in responseHeaders) {
      actual[kvp.Key] = kvp.Value;
    }

    var issues = new List<string>();

    foreach (var (expectedName, expectedRawValue) in expectedHeaders) {
      // Resolve variables in expected value
      var resolvedExpected = variableStore.ResolveVariables(expectedRawValue ?? string.Empty) ?? string.Empty;
      var expectedValue = (resolvedExpected ?? string.Empty).Trim();

      if (!actual.TryGetValue(expectedName, out var actualValueRaw)) {
        issues.Add($"missing header '{expectedName}'");
        continue;
      }

      var actualValue = (actualValueRaw ?? string.Empty).Trim();
      if (!string.Equals(expectedValue, actualValue, StringComparison.Ordinal)) {
        issues.Add($"header '{expectedName}' expected '{expectedValue}' but got '{actualValue}'");
      }
    }

    if (issues.Count > 0) {
      return (false, "Expected header(s) mismatch: " + string.Join("; ", issues));
    }

    return (true, string.Empty);
  }

  private static string ToDisplayString( object? value, bool isNull, bool isEmpty )
  {
    if (isNull)
      return "<null>";
    if (isEmpty)
      return "";
    return value?.ToString() ?? "";
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
  private static object ConvertJTokenToObject( JToken token )
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
