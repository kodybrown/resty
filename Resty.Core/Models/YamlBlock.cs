namespace Resty.Core.Models;

using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using YamlDotNet.Serialization;

/// <summary>
/// Unified model for all YAML code blocks found in Markdown files.
/// Can represent variable definitions, includes, or HTTP test definitions.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
public record YamlBlock
{
  /// <summary>
  /// Files to include for shared variables. Can be a single file or list of files.
  /// </summary>
  public List<string>? Include { get; init; }

  /// <summary>
  /// File-level mocks defined in this block (optional). Applies to tests in this file.
  /// </summary>
  public List<FileMockDefinition>? Mocks { get; init; }

  /// <summary>
  /// External JSON files containing mock definitions (list of FileMockDefinition objects).
  /// </summary>
  [YamlMember(Alias = "mocks_files", ApplyNamingConventions = false)]
  public List<string>? MocksFiles { get; init; }

  /// <summary>
  /// Variable definitions for this block.
  /// </summary>
  public Dictionary<string, object>? Variables { get; init; }

  /// <summary>
  /// Name of the test (if this block defines a test).
  /// </summary>
  public string? Test { get; init; }

  /// <summary>
  /// Optional human-readable description for the test.
  /// Included in all report formats.
  /// </summary>
  public string? Description { get; init; }

  /// <summary>
  /// Expectations for the response (e.g., expected status code).
  /// Matches the 'expect:' YAML section.
  /// </summary>
  public ExpectDefinition? Expect { get; init; }

  /// <summary>
  /// Whether this specific test/request must use a mock (and fail if none is found).
  /// </summary>
  [YamlMember(Alias = "mock_only", ApplyNamingConventions = false)]
  public bool MockOnly { get; init; } = false;

  /// <summary>
  /// Inline mock for this test (optional). If present, method/url are implied from this test.
  /// </summary>
  public InlineMockDefinition? Mock { get; init; }

  /// <summary>
  /// HTTP method (if this block defines a test).
  /// Can be GET, POST, PUT, DELETE, PATCH, etc.
  /// </summary>
  [YamlIgnore] // Only read from yaml, but never write to yaml.
  public string Method {
    get => _method;
    init => _method = value?.ToUpper() ?? string.Empty;
  }
  public string _method = string.Empty;

  /// <summary>
  /// Target URL (may contain variables to be resolved).
  /// </summary>
  [YamlIgnore] // Only read from yaml, but never write to yaml.
  public string Url { get; init; } = string.Empty;

  /// <summary>
  /// HTTP GET endpoint URL.
  /// </summary>
  [JsonIgnore]
  public string? Get {
    get => Method == "GET" ? Url : null;
    init { Method = "GET"; Url = value ?? string.Empty; }
  }

  /// <summary>
  /// HTTP POST endpoint URL.
  /// </summary>
  [JsonIgnore]
  public string? Post {
    get => Method == "POST" ? Url : null;
    init { Method = "POST"; Url = value ?? string.Empty; }
  }

  /// <summary>
  /// HTTP PUT endpoint URL.
  /// </summary>
  [JsonIgnore]
  public string? Put {
    get => Method == "PUT" ? Url : null;
    init { Method = "PUT"; Url = value ?? string.Empty; }
  }

  /// <summary>
  /// HTTP DELETE endpoint URL.
  /// </summary>
  [JsonIgnore]
  public string? Delete {
    get => Method == "DELETE" ? Url : null;
    init { Method = "DELETE"; Url = value ?? string.Empty; }
  }

  /// <summary>
  /// HTTP PATCH endpoint URL.
  /// </summary>
  [JsonIgnore]
  public string? Patch {
    get => Method == "PATCH" ? Url : null;
    init { Method = "PATCH"; Url = value ?? string.Empty; }
  }

  /// <summary>
  /// The content-type header value.
  /// This is a wrapper around Headers for backward compatibility.
  /// </summary>
  [JsonIgnore]
  [YamlIgnore] // Only read from yaml, but never write to yaml.
  public string ContentType {
    get {
      if (Headers is null || Headers.Count == 0) {
        return null;
      }
      var contentType = Headers.FirstOrDefault(h => string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase));
      return !string.IsNullOrEmpty(contentType.Key)
        ? contentType.Value
        : null;
    }
    init {
      var key = Headers.Keys.FirstOrDefault(k => string.Equals(k, "Content-Type", StringComparison.OrdinalIgnoreCase));
      if (value == null) {
        if (key != null) {
          Headers.Remove(key);
        }
      } else {
        if (key != null) {
          Headers[key] = value;
        } else {
          Headers["Content-Type"] = value;
        }
      }
    }
  }

  /// <summary>
  /// The authorization header value (may contain variables).
  /// This is a wrapper around Headers for backward compatibility.
  /// </summary>
  [JsonIgnore]
  [YamlIgnore] // Only read from yaml, but never write to yaml.
  public string? Authorization {
    get {
      if (Headers is null || Headers.Count == 0) {
        return null;
      }
      var (key, value) = Headers.FirstOrDefault(h => string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase));
      return !string.IsNullOrEmpty(key)
        ? value
        : null;
    }
    init {
      var key = Headers.Keys.FirstOrDefault(k => string.Equals(k, "Authorization", StringComparison.OrdinalIgnoreCase));
      if (value == null) {
        if (key != null) {
          Headers.Remove(key);
        }
      } else {
        if (key != null) {
          Headers[key] = value;
        } else {
          Headers["Authorization"] = value;
        }
      }
    }
  }

  /// <summary>
  /// All HTTP headers (may contain variables in values).
  /// </summary>
  public Dictionary<string, string>? Headers { get; init; }

  /// <summary>
  /// HTTP request body content. Can be a string (raw) or a structured mapping/array.
  /// When structured, it will be serialized based on Content-Type.
  /// </summary>
  public object? Body { get; init; }

  /// <summary>
  /// Response capture definitions for extracting values from responses.
  /// Key = variable name, Value = JSON path or simple path to extract.
  /// </summary>
  public Dictionary<string, string>? Capture { get; init; }

  /// <summary>
  /// Whether this test is disabled and should be skipped during execution.
  /// </summary>
  public bool Disabled { get; init; } = false;

  /// <summary>
  /// Number of times to retry the HTTP request if it fails (0 means no retries).
  /// </summary>
  public int Retry { get; init; } = 0;

  /// <summary>
  /// HTTP request timeout in seconds for this test. If null, uses default timeout.
  /// </summary>
  public int? Timeout { get; init; }

  /// <summary>
  /// List of test names this test depends on. These tests must run successfully before this test runs.
  /// Can be a single test name string or an array of test names.
  /// </summary>
  public List<string>? Requires { get; init; }

  /// <summary>
  /// Non-test block dependencies that must be executed before any tests in this file.
  /// Intended for use in configuration blocks alongside 'include' to define shared setup.
  /// </summary>
  public List<string>? Dependencies { get; init; }

  /// <summary>
  /// Determines if this block represents an HTTP test.
  /// A test must have a test name and exactly one HTTP method.
  /// </summary>
  public bool IsTest
    => !string.IsNullOrWhiteSpace(Test)
    && (
      (!string.IsNullOrEmpty(Method) && !string.IsNullOrEmpty(Url))
      || (Mock != null)
      || (MockOnly)
    );

  /// <summary>
  /// Determines if this block contains variables or includes.
  /// </summary>
  public bool HasVariableData => Variables?.Count > 0 || Include?.Count > 0;

  /// <summary>
  /// Gets the HTTP method and URL for this test.
  /// </summary>
  /// <returns>Tuple of (Method, Url) or null if not a valid test.</returns>
  public (string Method, string Url)? GetHttpMethodAndUrl()
  {
    var methods = GetHttpMethods();
    if (methods.Count != 1) {
      return null;
    }

    var (method, url) = methods.First();
    return (method.ToUpper(), url);
  }

  /// <summary>
  /// Gets all defined HTTP methods and their URLs.
  /// </summary>
  private Dictionary<string, string> GetHttpMethods()
  {
    var methods = new Dictionary<string, string>();

    if (!string.IsNullOrWhiteSpace(Get)) {
      methods["GET"] = Get;
    }
    if (!string.IsNullOrWhiteSpace(Post)) {
      methods["POST"] = Post;
    }
    if (!string.IsNullOrWhiteSpace(Put)) {
      methods["PUT"] = Put;
    }
    if (!string.IsNullOrWhiteSpace(Delete)) {
      methods["DELETE"] = Delete;
    }
    if (!string.IsNullOrWhiteSpace(Patch)) {
      methods["PATCH"] = Patch;
    }

    return methods;
  }
}

/// <summary>
/// Inline mock definition for a single test.
/// </summary>
public record InlineMockDefinition
{
  public object? Status { get; init; }
  public Dictionary<string, string>? Headers { get; init; }
  public object? Body { get; init; }
  public string? ContentType { get; init; }
  public int? DelayMs { get; init; }
  public List<MockResponse>? Sequence { get; init; }
}

/// <summary>
/// File-level mock definition; requires method and url.
/// </summary>
public record FileMockDefinition
{
  public string Method { get; init; } = string.Empty;
  public string Url { get; init; } = string.Empty;
  public object? Status { get; init; }
  public Dictionary<string, string>? Headers { get; init; }
  public object? Body { get; init; }
  public string? ContentType { get; init; }
  public int? DelayMs { get; init; }
  public List<MockResponse>? Sequence { get; init; }
}

/// <summary>
/// A mock response element (single response within a sequence or standalone).
/// </summary>
public record MockResponse
{
  public object? Status { get; init; }
  public Dictionary<string, string>? Headers { get; init; }
  public object? Body { get; init; }
  public string? ContentType { get; init; }
  public int? DelayMs { get; init; }
}

/// <summary>
/// Expectations definition for a test's response.
/// </summary>
public record ExpectDefinition
{
  /// <summary>
  /// Expected HTTP status code (e.g., 200, 404). If not specified, defaults to 2xx success semantics.
  /// </summary>
  public int? Status { get; init; }

  /// <summary>
  /// Expected response headers (case-insensitive names, case-sensitive values).
  /// Values may contain variables to be resolved.
  /// </summary>
  public Dictionary<string, string>? Headers { get; init; }

  /// <summary>
  /// Expected JSON value assertions evaluated against the response body.
  /// </summary>
  public List<ValueExpectation>? Values { get; init; }
}

/// <summary>
/// A rule describing a JSONPath extraction and comparison against an expected value.
/// </summary>
public record ValueExpectation
{
  /// <summary>
  /// JSONPath expression to extract value(s) from the response JSON.
  /// </summary>
  public string Key { get; init; } = string.Empty;

  /// <summary>
  /// Operation to perform: equals, not_equals, contains, not_contains, startswith, not_startswith,
  /// endswith, not_endswith, greater_than, greater_than_or_equal, less_than, less_than_or_equal,
  /// exists, not_exists. Short aliases are supported (eq, ne, gt, gte, lt, lte, starts_with, ends_with).
  /// </summary>
  public string Op { get; init; } = string.Empty;

  /// <summary>
  /// Expected value to compare with. Optional for exists/not_exists.
  /// May contain variables, and supports keywords: $null, $empty.
  /// </summary>
  public object? Value { get; init; }

  /// <summary>
  /// When provided, stores the extracted value (first matching) into a variable after evaluation.
  /// </summary>
  public string? StoreAs { get; init; }

  /// <summary>
  /// Whether to ignore case for string comparisons. Default true when null.
  /// </summary>
  public bool? IgnoreCase { get; init; }
}
