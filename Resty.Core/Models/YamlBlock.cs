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
  /// Variable definitions for this block.
  /// </summary>
  public Dictionary<string, object>? Variables { get; init; }

  /// <summary>
  /// Name of the test (if this block defines a test).
  /// </summary>
  public string? Test { get; init; }

  /// <summary>
  /// Expectations for the response (e.g., expected status code).
  /// Matches the 'expect:' YAML section.
  /// </summary>
  public ExpectDefinition? Expect { get; init; }

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
  /// HTTP request body content.
  /// </summary>
  public string? Body { get; init; }

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
  /// Determines if this block represents an HTTP test.
  /// A test must have a test name and exactly one HTTP method.
  /// </summary>
  public bool IsTest
    => !string.IsNullOrWhiteSpace(Test)
    // && GetHttpMethods().Count == 1
    && !string.IsNullOrEmpty(Method)
    && !string.IsNullOrEmpty(Url);

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
}
