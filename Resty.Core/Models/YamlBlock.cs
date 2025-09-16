namespace Resty.Core.Models;

/// <summary>
/// Unified model for all YAML code blocks found in Markdown files.
/// Can represent variable definitions, includes, or HTTP test definitions.
/// </summary>
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
  /// HTTP GET endpoint URL.
  /// </summary>
  public string? Get { get; init; }

  /// <summary>
  /// HTTP POST endpoint URL.
  /// </summary>
  public string? Post { get; init; }

  /// <summary>
  /// HTTP PUT endpoint URL.
  /// </summary>
  public string? Put { get; init; }

  /// <summary>
  /// HTTP DELETE endpoint URL.
  /// </summary>
  public string? Delete { get; init; }

  /// <summary>
  /// HTTP PATCH endpoint URL.
  /// </summary>
  public string? Patch { get; init; }

  /// <summary>
  /// Content-Type header value. Defaults to "application/json".
  /// </summary>
  public string? ContentType { get; init; }

  /// <summary>
  /// Authorization header value (e.g., "Bearer token123").
  /// </summary>
  public string? Authorization { get; init; }

  /// <summary>
  /// Additional HTTP headers.
  /// </summary>
  public Dictionary<string, string>? Headers { get; init; }

  /// <summary>
  /// HTTP request body content.
  /// </summary>
  public string? Body { get; init; }

  /// <summary>
  /// Response capture definitions for extracting values from successful responses.
  /// Key = variable name, Value = JSON path or simple path to extract.
  /// </summary>
  public Dictionary<string, string>? Success { get; init; }

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
  /// Determines if this block represents an HTTP test.
  /// A test must have a test name and exactly one HTTP method.
  /// </summary>
  public bool IsTest => !string.IsNullOrWhiteSpace(Test) && GetHttpMethods().Count == 1;

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
