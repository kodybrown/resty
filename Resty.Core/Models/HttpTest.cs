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
  /// Optional human-readable description for the test.
  /// </summary>
  public string? Description { get; init; }

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
  /// HTTP request body content (string) after variable resolution for legacy/raw usage.
  /// For structured bodies, see RawBody which will be serialized later.
  /// </summary>
  public string? Body { get; init; }

  /// <summary>
  /// Raw body as parsed from YAML. Can be string, dictionary, list, or null.
  /// </summary>
  public object? RawBody { get; init; }

  /// <summary>
  /// Parts of a multipart form request, in the order they are sent.
  /// </summary>
  public IReadOnlyList<MultipartPartDefinition>? Multipart { get; init; }

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
    get => Expect?.Status?.Codes.Count == 1 ? Expect.Status.Codes[0] : null;
    init {
      if (value.HasValue) {
        Expect = (Expect ?? new ExpectDefinition()) with { Status = value.Value };
      }
    }
  }

  /// <summary>
  /// Acceptable HTTP status codes for this test. When null, default success
  /// semantics (2xx) are used.
  /// </summary>
  public IReadOnlyList<int>? ExpectedStatuses => Expect?.Status?.Codes;

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
  /// Delay applied once before the first request attempt.
  /// </summary>
  public object? WaitBefore { get; init; }

  /// <summary>
  /// Retry policy for this test.
  /// </summary>
  public RetryPolicyDefinition? Retry { get; init; }

  // Mocking
  //

  /// <summary>
  /// If true, this test will not perform a real HTTP request and will only use mocks, regardless
  /// of whether a method and URL are specified, and regardless if `--mock` was specified on the CLI. If false (default), the test performs a real HTTP request unless `--mock` was specified on the CLI.
  /// </summary>
  public bool MockOnly { get; init; } = false;

  /// <summary>
  /// Inline mock definition for this test (if any).
  /// </summary>
  public InlineMockDefinition? InlineMock { get; init; }

  /// <summary>
  /// List of file-based mock definitions associated with this test (if any).
  /// These mock responses are matched based on the test's method and URL,
  /// and are only used if MockOnly is true or if --mock was specified on the CLI.
  /// They are also not available outside of this file.
  /// </summary>
  public List<FileMockDefinition> FileMocks { get; init; } = new();

  /// <summary>
  /// List of external mock files to load for this test (if any).
  /// These files can contain multiple mock definitions that can be matched
  /// based on the test's method and URL. They are only used if MockOnly is true
  /// or if --mock was specified on the CLI.
  /// Mock files (.json) _are_ available outside of the file they were defined in.
  /// </summary>
  public List<string> MockFiles { get; init; } = new();

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
    if (block.Multipart is { } parts) {
      ValidateMultipart(block, parts);
    }

    var httpTest = new HttpTest {
      Name = block.Test!,
      Method = methodAndUrl?.Method ?? string.Empty,
      Url = methodAndUrl?.Url ?? string.Empty,
      Description = block.Description,
      ContentType = block.Multipart is null ? block.ContentType ?? "application/json" : "multipart/form-data",
      Authorization = block.Authorization,
      Headers = block.Headers ?? new Dictionary<string, string>(),
      // If Body is a string, assign to Body; RawBody always gets the original
      Body = block.Body as string,
      RawBody = block.Body,
      Multipart = block.Multipart,
      Extractors = block.Capture ?? new Dictionary<string, string>(),
      Expect = block.Expect,
      SourceFile = sourceFile,
      SourceLine = sourceLine,
      Timeout = block.Timeout,
      WaitBefore = block.WaitBefore,
      Retry = block.Retry,
      MockOnly = block.MockOnly,
      InlineMock = block.Mock
    };

    // Validate mock_only without method/url and without inline mock
    if (httpTest.MockOnly && httpTest.InlineMock is null && (string.IsNullOrEmpty(httpTest.Method) || string.IsNullOrEmpty(httpTest.Url))) {
      throw new InvalidOperationException("mock_only requires either an inline mock or an HTTP method+url to match file-level mocks.");
    }

    return httpTest;
  }

  private static void ValidateMultipart( YamlBlock block, IReadOnlyList<MultipartPartDefinition> parts )
  {
    if (block.Body is not null || block.ContentType is not null) {
      throw new InvalidOperationException("multipart cannot be combined with body or a request-level Content-Type header.");
    }
    if (parts.Count == 0) {
      throw new InvalidOperationException("multipart must contain at least one part.");
    }

    foreach (var part in parts) {
      if (part is null || string.IsNullOrWhiteSpace(part.Name)) {
        throw new InvalidOperationException("Each multipart part must have a name.");
      }
      if ((part.Value is null) == (part.File is null) || (part.File is not null && string.IsNullOrWhiteSpace(part.File))) {
        throw new InvalidOperationException($"Multipart part '{part.Name}' must specify either a value or a nonempty file path.");
      }
      if (part.Value is not null && part.ContentType is not null) {
        throw new InvalidOperationException($"Multipart part '{part.Name}' can specify content_type only for a file.");
      }
    }
  }
}
