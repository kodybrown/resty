namespace Resty.Core.Execution;

using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
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
  private readonly bool _enableMocking;
  private readonly Dictionary<string, int> _sequencePositions = new();

  public HttpTestExecutor( HttpClient httpClient, bool enableMocking = false )
  {
    _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    _enableMocking = enableMocking;
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

        // Step 3: Decide whether to mock this request and possibly produce a response
        HttpResponseMessage? response = null;
        var shouldAttemptMock = ShouldAttemptMock(test);
        if (shouldAttemptMock) {
          response = await TryServeMockAsync(test, resolvedTest, variableStore, cancellationToken);
        }

        if (response == null) {
          // Step 3.1: Create HTTP request (only if not mocked)
          var request = CreateHttpRequest(resolvedTest);

          // Step 4: Execute HTTP request with timeout
          using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
          if (timeoutSeconds.HasValue) {
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds.Value));
          }

          response = await _httpClient.SendAsync(request, timeoutCts.Token);
        }

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

  private bool ShouldAttemptMock( HttpTest test )
  {
    // Attempt mock if inline mock is present, or test is mock_only, or global mocking is enabled
    if (test.InlineMock != null) { return true; }
    if (test.MockOnly) { return true; }
    if (_enableMocking) { return true; }
    // Opportunistically attempt mocks when file-level mocks are available for this test
    if ((test.FileMocks?.Count ?? 0) > 0 || (test.MockFiles?.Count ?? 0) > 0) { return true; }
    return false;
  }

  private async Task<HttpResponseMessage?> TryServeMockAsync(
      HttpTest test,
      ResolvedHttpTest resolvedTest,
      VariableStore vars,
      CancellationToken cancellationToken )
  {
    // Inline mock takes precedence
    if (test.InlineMock != null) {
      return await BuildResponseFromInlineMockAsync(test, vars, cancellationToken);
    }

    // If we don't have method+url and no inline mock, cannot match file-level mocks
    if (string.IsNullOrEmpty(resolvedTest.Method) || string.IsNullOrEmpty(resolvedTest.Url)) {
      if (test.MockOnly) {
        throw new InvalidOperationException("mock_only test without method/url must provide an inline mock.");
      }
      return null; // fall back to network (if allowed)
    }

    // Attempt file-level mocks from test.FileMocks and test.MockFiles (last-wins)
    var method = resolvedTest.Method.ToUpperInvariant();
    var url = resolvedTest.Url;

    // Build merged list: mocks_files then inline list; last wins → we will search from end to start
    var merged = new List<FileMockDefinition>();

    // Load mocks from files
    if (test.MockFiles?.Count > 0) {
      // Track duplicates across mock files (method+url); warn when seen again
      var seenAcrossFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var f in test.MockFiles) {
        var baseDir = Path.GetDirectoryName(test.SourceFile) ?? string.Empty;
        var full = Path.IsPathRooted(f) ? f : Path.GetFullPath(Path.Combine(baseDir, f));
        var list = LoadMockFileList(full, test.SourceFile);
        if (list != null && list.Count > 0) {
          foreach (var entry in list) {
            var rawKey = $"{(entry.Method ?? string.Empty).ToUpperInvariant()}|{entry.Url ?? string.Empty}";
            if (!seenAcrossFiles.Add(rawKey)) {
              Console.WriteLine($"Warning: duplicate mock for {rawKey} in mocks_files. Last definition wins. (File: {full})");
            }
            merged.Add(entry);
          }
        }
      }
    }

    if (test.FileMocks?.Count > 0) {
      merged.AddRange(test.FileMocks);
    }

    for (int i = merged.Count - 1; i >= 0; i--) {
      var entry = merged[i];
      if (!string.Equals(entry.Method, method, StringComparison.OrdinalIgnoreCase)) { continue; }
      var entryUrl = vars.ResolveVariables(entry.Url ?? string.Empty) ?? string.Empty;
      if (!string.Equals(entryUrl, url, StringComparison.Ordinal)) { continue; }

      return await BuildResponseFromFileMockAsync(method, url, entry, vars, cancellationToken);
    }

    if (test.MockOnly) {
      throw new InvalidOperationException($"No mock found for {method} {url} (mock_only)");
    }

    return null; // No mock served → fall through to network
  }

  private async Task<HttpResponseMessage> BuildResponseFromInlineMockAsync( HttpTest test, VariableStore vars, CancellationToken ct )
  {
    var mk = test.InlineMock!;

    if (mk.Sequence != null && mk.Sequence.Count > 0) {
      var key = $"{test.SourceFile}::{test.Name}::inline";
      var idx = NextSequenceIndex(key, mk.Sequence.Count);
      var elem = mk.Sequence[Math.Min(idx, mk.Sequence.Count - 1)];
      var status = CoerceStatus(elem.Status ?? mk.Status, vars, 200);
      return await BuildResponseAsync(status, elem.Headers ?? mk.Headers, elem.Body ?? mk.Body, elem.ContentType ?? mk.ContentType, elem.DelayMs ?? mk.DelayMs, vars, ct);
    }

    var baseStatus = CoerceStatus(mk.Status, vars, 200);
    return await BuildResponseAsync(baseStatus, mk.Headers, mk.Body, mk.ContentType, mk.DelayMs, vars, ct);
  }

  private async Task<HttpResponseMessage> BuildResponseFromFileMockAsync( string method, string url, FileMockDefinition entry, VariableStore vars, CancellationToken ct )
  {
    if (entry.Sequence != null && entry.Sequence.Count > 0) {
      var key = $"{method}|{url}";
      var idx = NextSequenceIndex(key, entry.Sequence.Count);
      var elem = entry.Sequence[Math.Min(idx, entry.Sequence.Count - 1)];
      var status = CoerceStatus(elem.Status ?? entry.Status, vars, 200);
      return await BuildResponseAsync(status, elem.Headers ?? entry.Headers, elem.Body ?? entry.Body, elem.ContentType ?? entry.ContentType, elem.DelayMs ?? entry.DelayMs, vars, ct);
    }

    var baseStatus = CoerceStatus(entry.Status, vars, 200);
    return await BuildResponseAsync(baseStatus, entry.Headers, entry.Body, entry.ContentType, entry.DelayMs, vars, ct);
  }

  private int NextSequenceIndex( string key, int count )
  {
    if (!_sequencePositions.TryGetValue(key, out var pos)) {
      pos = 0;
    } else {
      pos++;
    }
    _sequencePositions[key] = pos;
    // sticky-last behavior is implemented by capping index when selecting
    return pos;
  }

  private async Task<HttpResponseMessage> BuildResponseAsync(
      int status,
      Dictionary<string, string>? headers,
      object? body,
      string? contentType,
      int? delayMs,
      VariableStore vars,
      CancellationToken ct )
  {
    if (delayMs.HasValue && delayMs.Value > 0) {
      await Task.Delay(delayMs.Value, ct);
    }

    var response = new HttpResponseMessage((HttpStatusCode)status);

    // Prepare content without setting media type to avoid duplicate Content-Type values
    HttpContent? content = null;
    string? effectiveContentType = null;
    if (body is null) {
      content = new StringContent(string.Empty, Encoding.UTF8);
      effectiveContentType = contentType; // may be overridden by headers
    } else if (body is string s) {
      var resolved = vars.ResolveVariables(s) ?? string.Empty;
      content = new StringContent(resolved, Encoding.UTF8);
      effectiveContentType = contentType ?? "text/plain";
    } else {
      var resolvedStruct = ResolveStructuredVariablesDeep(body, vars);
      var json = JsonConvert.SerializeObject(resolvedStruct);
      content = new StringContent(json, Encoding.UTF8);
      effectiveContentType = contentType ?? "application/json";
    }

    response.Content = content;

    // Prefer Content-Type provided in headers over computed effectiveContentType
    string? headerContentType = null;
    if (headers != null) {
      foreach (var (hk, hv) in headers) {
        if (string.Equals(hk, "Content-Type", StringComparison.OrdinalIgnoreCase)) {
          headerContentType = hv;
          break;
        }
      }
    }
    var finalContentType = headerContentType ?? effectiveContentType ?? "application/json";
    if (response.Content != null && !string.IsNullOrWhiteSpace(finalContentType)) {
      if (System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(finalContentType, out var parsedCt)) {
        response.Content.Headers.ContentType = parsedCt;
      } else {
        response.Content.Headers.Remove("Content-Type");
        response.Content.Headers.TryAddWithoutValidation("Content-Type", finalContentType);
      }
    }

    // Headers
    if (headers != null) {
      foreach (var (k, v) in headers) {
        if (string.Equals(k, "Content-Type", StringComparison.OrdinalIgnoreCase)) {
          // Already applied above as finalContentType
          continue;
        }
        response.Headers.TryAddWithoutValidation(k, v);
      }
    }

    return response;
  }

  private static List<FileMockDefinition>? LoadMockFileList( string path, string sourceFile )
  {
    try {
      var baseDir = Path.GetDirectoryName(sourceFile) ?? string.Empty;
      var full = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDir, path));
      if (!File.Exists(full)) { return null; }
      var json = File.ReadAllText(full);
      var list = JsonConvert.DeserializeObject<List<FileMockDefinition>>(json);
      return list;
    } catch {
      return null;
    }
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

  private static int CoerceStatus( object? status, VariableStore vars, int fallback )
  {
    return TryCoerceToInt(status, vars, out var value) ? value : fallback;
  }

  private static bool TryCoerceToInt( object? value, VariableStore vars, out int result )
  {
    result = default;
    if (value is null) { return false; }
    switch (value) {
      case int i:
        result = i; return true;
      case long l:
        result = (int)l; return true;
      case double d:
        result = (int)d; return true;
      case string s:
        var resolved = vars.ResolveVariables(s) ?? string.Empty;
        return int.TryParse(resolved, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result);
      default:
        var text = value.ToString() ?? string.Empty;
        return int.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out result);
    }
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
      case Newtonsoft.Json.Linq.JValue jv:
        return jv.Value == null ? null! : ResolveStructuredVariablesDeep(jv.Value, vars);
      case Newtonsoft.Json.Linq.JObject jo:
        var dict = new Dictionary<string, object?>();
        foreach (var p in jo.Properties()) {
          dict[p.Name] = p.Value == null ? null : ResolveStructuredVariablesDeep(p.Value, vars);
        }
        return dict;
      case Newtonsoft.Json.Linq.JArray ja:
        return ja.Select(token => token == null ? null : ResolveStructuredVariablesDeep(token, vars)).ToList();
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

      // Compatibility: treat YAML null expected for *.type() as string "null"
      if (!string.IsNullOrWhiteSpace(key) && key.TrimEnd().EndsWith(".type()", StringComparison.OrdinalIgnoreCase) && expIsNull) {
        expValue = "null";
        expIsNull = false;
        expIsEmpty = false;
      }

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
    // Support a simple chain of zero-argument postfix functions applied to the result of a base JSONPath.
    // Example: $.items.keys().length() → evaluate $.items, then apply keys(), then apply length().
    // This keeps compatibility with Newtonsoft's JSONPath while enabling simple function chaining.

    // Parse trailing chain of .func() segments, right-to-left, collecting in left-to-right order.
    var funcs = new List<string>();
    var remaining = jsonPath;
    while (true) {
      var m = Regex.Match(remaining, @"\.(\w+)\(\)$");
      if (!m.Success) { break; }
      var name = (m.Groups[1].Value ?? string.Empty).Trim().ToLowerInvariant();
      funcs.Insert(0, name);
      remaining = remaining.Substring(0, remaining.Length - m.Length);
    }

    IEnumerable<JToken> BaseSelect(string path)
    {
      try {
        return json.SelectTokens(path) ?? Enumerable.Empty<JToken>();
      } catch {
        // Fallback to single token selection to handle bad paths
        var t = json.SelectToken(path);
        return t != null ? new[] { t } : Enumerable.Empty<JToken>();
      }
    }

    // If no trailing functions, use the legacy logic.
    if (funcs.Count == 0) {
      return BaseSelect(jsonPath);
    }

    // Evaluate base tokens, then apply each function in order.
    var tokens = BaseSelect(remaining).ToList();

    foreach (var f in funcs) {
      tokens = ApplyJsonPathFunction(f, tokens).ToList();
    }

    return tokens;
  }

  private static IEnumerable<JToken> ApplyJsonPathFunction( string func, IEnumerable<JToken> tokens )
  {
    switch (func) {
      // length-family → numeric scalar
      case "length":
      case "count":
      case "size":
        return tokens.Select(t => new JValue(ComputeLength(t)));

      // empty → boolean scalar
      case "empty":
        return tokens.Select(t => new JValue(IsEmpty(t)));

      // type → string scalar
      case "type":
        return tokens.Select(t => new JValue(GetTypeName(t)));

      // Aggregates on arrays → numeric scalar
      case "sum":
        return tokens.Select(t => new JValue(AggregateNumbers(t, Aggregation.Sum)));
      case "avg":
        return tokens.Select(t => new JValue(AggregateNumbers(t, Aggregation.Avg)));
      case "min":
        return tokens.Select(t => new JValue(AggregateNumbers(t, Aggregation.Min)));
      case "max":
        return tokens.Select(t => new JValue(AggregateNumbers(t, Aggregation.Max)));

      // Array/object transforms → array token
      case "distinct":
        return tokens.Select(t => MakeDistinctArray(t));
      case "keys":
        return tokens.Select(t => GetObjectKeysArray(t));
      case "values":
        return tokens.Select(t => GetObjectValuesArray(t));

      // Conversions → scalar or mapped array (kept as token, not flattened)
      case "to_number":
        return tokens.Select(t => ToNumberToken(t));
      case "to_string":
        return tokens.Select(t => ToStringToken(t));
      case "to_boolean":
        return tokens.Select(t => ToBooleanToken(t));

      // String utilities
      case "trim":
        return tokens.Select(t => StringTransformToken(t, s => s?.Trim() ?? string.Empty));
      case "lower":
        return tokens.Select(t => StringTransformToken(t, s => (s ?? string.Empty).ToLowerInvariant()));
      case "upper":
        return tokens.Select(t => StringTransformToken(t, s => (s ?? string.Empty).ToUpperInvariant()));

      default:
        // Unknown function → return tokens unchanged to avoid hard failures
        return tokens;
    }
  }

  private enum Aggregation { Sum, Avg, Min, Max }

  private static int ComputeLength( JToken t )
  {
    return t.Type switch {
      JTokenType.Array => ((JArray)t).Count,
      JTokenType.String => (t.Value<string>() ?? string.Empty).Length,
      JTokenType.Null => 0,
      _ => 0
    };
  }

  private static bool IsEmpty( JToken t )
  {
    return t.Type switch {
      JTokenType.Null => true,
      JTokenType.Array => ((JArray)t).Count == 0,
      JTokenType.String => string.IsNullOrEmpty(t.Value<string>()),
      JTokenType.Object => !((JObject)t).Properties().Any(),
      _ => false
    };
  }

  private static string GetTypeName( JToken t )
  {
    // Robust type detection that treats numeric/boolean strings as their logical types
    switch (t.Type) {
      case JTokenType.Array: return "array";
      case JTokenType.Object: return "object";
      case JTokenType.Integer:
      case JTokenType.Float: return "number";
      case JTokenType.Boolean: return "boolean";
      case JTokenType.Null: return "null";
      case JTokenType.Date: return "date";
      case JTokenType.String: {
        var s = t.Value<string>() ?? string.Empty;
        if (bool.TryParse(s, out _)) { return "boolean"; }
        if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _)) { return "number"; }
        if (string.Equals(s, "null", StringComparison.OrdinalIgnoreCase)) { return "null"; }
        return "string";
      }
      default:
        return t.Type.ToString().ToLowerInvariant();
    }
  }

  private static double AggregateNumbers( JToken t, Aggregation agg )
  {
    if (t.Type != JTokenType.Array) { return 0d; }
    var arr = (JArray)t;
    var values = new List<double>();
    foreach (var item in arr) {
      if (TryParseDoubleFromToken(item, out var d)) {
        values.Add(d);
      }
    }
    if (values.Count == 0) { return 0d; }
    return agg switch {
      Aggregation.Sum => values.Sum(),
      Aggregation.Avg => values.Average(),
      Aggregation.Min => values.Min(),
      Aggregation.Max => values.Max(),
      _ => 0d
    };
  }

  private static bool TryParseDoubleFromToken( JToken t, out double d )
  {
    d = 0d;
    switch (t.Type) {
      case JTokenType.Integer:
        d = t.Value<long>();
        return true;
      case JTokenType.Float:
        d = t.Value<double>();
        return true;
      case JTokenType.Boolean:
        d = t.Value<bool>() ? 1d : 0d;
        return true;
      case JTokenType.String:
        return double.TryParse(t.Value<string>(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d);
      default:
        return false;
    }
  }

  private static JToken MakeDistinctArray( JToken t )
  {
    if (t.Type != JTokenType.Array) { return t; }
    var arr = (JArray)t;
    var distinct = arr.Distinct(new JTokenEqualityComparer());
    return new JArray(distinct);
  }

  private static JToken GetObjectKeysArray( JToken t )
  {
    if (t.Type != JTokenType.Object) { return t; }
    var obj = (JObject)t;
    return new JArray(obj.Properties().Select(p => new JValue(p.Name)));
  }

  private static JToken GetObjectValuesArray( JToken t )
  {
    if (t.Type != JTokenType.Object) { return t; }
    var obj = (JObject)t;
    return new JArray(obj.Properties().Select(p => p.Value));
  }

  private static JToken ToNumberToken( JToken t )
  {
    if (t.Type == JTokenType.Array) {
      // Map array elements to numbers without flattening
      var arr = (JArray)t;
      var mapped = new JArray(arr.Select(e => ToNumberToken(e)));
      return mapped;
    }

    switch (t.Type) {
      case JTokenType.Integer:
      case JTokenType.Float:
        return t;
      case JTokenType.Boolean:
        return new JValue(t.Value<bool>() ? 1d : 0d);
      case JTokenType.Null:
        return new JValue(0d);
      default:
        var s = t.Type == JTokenType.String ? t.Value<string>() : t.ToString(Newtonsoft.Json.Formatting.None);
        if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)) {
          return new JValue(d);
        }
        return new JValue(double.NaN);
    }
  }

  private static JToken ToStringToken( JToken t )
  {
    if (t.Type == JTokenType.Array) {
      var arr = (JArray)t;
      return new JArray(arr.Select(e => ToStringToken(e)));
    }
    if (t.Type == JTokenType.String) { return t; }
    if (t.Type == JTokenType.Null) { return new JValue(string.Empty); }
    return new JValue(t.ToString(Newtonsoft.Json.Formatting.None));
  }

  private static JToken ToBooleanToken( JToken t )
  {
    if (t.Type == JTokenType.Array) {
      var arr = (JArray)t;
      return new JArray(arr.Select(e => ToBooleanToken(e)));
    }

    switch (t.Type) {
      case JTokenType.Boolean:
        return t;
      case JTokenType.Integer:
        return new JValue(t.Value<long>() != 0);
      case JTokenType.Float:
        return new JValue(Math.Abs(t.Value<double>()) > double.Epsilon);
      case JTokenType.String:
        var s = t.Value<string>() ?? string.Empty;
        if (bool.TryParse(s, out var b)) { return new JValue(b); }
        if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)) {
          return new JValue(Math.Abs(d) > double.Epsilon);
        }
        return new JValue(false);
      case JTokenType.Null:
        return new JValue(false);
      default:
        return new JValue(true); // objects become truey
    }
  }

  private static JToken StringTransformToken( JToken t, Func<string?, string> transform )
  {
    if (t.Type == JTokenType.Array) {
      var arr = (JArray)t;
      return new JArray(arr.Select(e => StringTransformToken(e, transform)));
    }

    var s = t.Type == JTokenType.String ? t.Value<string>() : t.ToString(Newtonsoft.Json.Formatting.None);
    return new JValue(transform(s));
  }

  private static (object? Value, bool IsNull, bool IsEmpty) ResolveExpectedValue( object? raw, VariableStore vars )
  {
    if (raw == null) { return (null, true, false); }

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

  private static bool TryGetDate( JToken token, out DateTimeOffset value )
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

  private static bool TryCoerceToDate( object? expected, out DateTimeOffset value )
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
    if (isNull) {
      return "<null>";
    }
    if (isEmpty) {
      return "";
    }

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
