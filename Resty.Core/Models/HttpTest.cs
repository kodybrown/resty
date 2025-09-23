namespace Resty.Core.Models;

/// <summary>
/// Represents a parsed and validated HTTP test definition.
/// </summary>
public record HttpTest
{
  /// <summary>
  /// Name of the test for identification and filtering.
  /// </summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>
  /// HTTP method (GET, POST, PUT, DELETE, PATCH).
  /// </summary>
  public string Method { get; init; } = string.Empty;

  /// <summary>
  /// Target URL (may contain variables to be resolved).
  /// </summary>
  public string Url { get; init; } = string.Empty;

  /// <summary>
  /// Content-Type header value.
  /// </summary>
  public string ContentType { get; init; } = "application/json";

  /// <summary>
  /// Authorization header value (may contain variables).
  /// </summary>
  public string? Authorization { get; init; }

  /// <summary>
  /// Additional HTTP headers (may contain variables in values).
  /// </summary>
  public Dictionary<string, string> Headers { get; init; } = new();

  /// <summary>
  /// HTTP request body content (may contain variables).
  /// </summary>
  public string? Body { get; init; }

  /// <summary>
  /// Response extractors for capturing values from successful responses.
  /// Key = variable name to create, Value = JSON path or extraction expression.
  /// </summary>
  public Dictionary<string, string> Extractors { get; init; } = new();

  /// <summary>
  /// Expectations for the response (e.g., expected status code).
  /// </summary>
  public ExpectDefinition? Expect { get; init; }

  /// <summary>
  /// Expected HTTP status code for this test (optional). When set, the test passes
  /// if and only if the actual response status equals this value. When null,
  /// default success semantics (2xx) are used.
  /// </summary>
  public int? ExpectedStatus {
    get => Expect?.Status;
    init => Expect ??= new() { Status = value };
  }

  /// <summary>
  /// File path where this test was defined (for error reporting).
  /// </summary>
  public string SourceFile { get; init; } = string.Empty;

  /// <summary>
  /// Line number in the source file where this test was defined.
  /// </summary>
  public int SourceLine { get; init; }

  /// <summary>
  /// HTTP request timeout in seconds for this test. If null, uses default timeout.
  /// </summary>
  public int? Timeout { get; init; }

  /// <summary>
  /// Creates an HttpTest from a validated YamlBlock.
  /// </summary>
  /// <param name="block">The YAML block containing the test definition.</param>
  /// <param name="sourceFile">Source file path for error reporting.</param>
  /// <param name="sourceLine">Source line number for error reporting.</param>
  /// <returns>A new HttpTest instance.</returns>
  /// <exception cref="ArgumentException">Thrown if the block is not a valid test.</exception>
  public static HttpTest FromYamlBlock( YamlBlock block, string sourceFile, int sourceLine )
  {
    if (!block.IsTest) {
      throw new ArgumentException("YamlBlock is not a valid test", nameof(block));
    }

    var methodAndUrl = block.GetHttpMethodAndUrl()!;

    return new HttpTest {
      Name = block.Test!,
      Method = methodAndUrl.Value.Method,
      Url = methodAndUrl.Value.Url,
      ContentType = block.ContentType ?? "application/json",
      Authorization = block.Authorization,
      Headers = block.Headers ?? new Dictionary<string, string>(),
      Body = block.Body,
      Extractors = block.Capture ?? new Dictionary<string, string>(),
      SourceFile = sourceFile,
      SourceLine = sourceLine,
      Timeout = block.Timeout
    };
  }
}
