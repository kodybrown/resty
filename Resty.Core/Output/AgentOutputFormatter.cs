namespace Resty.Core.Output;

using System.Reflection;
using System.Text.Json;
using Resty.Core.Models;

/// <summary>
/// Formats test results as compact, failure-first JSON for agents and automation.
/// </summary>
public class AgentOutputFormatter : IOutputFormatter
{
  private const int DefaultPreviewLength = 2048;
  private static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = false,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };

  public void WriteToConsole( TestRunSummary summary, bool verbose = false, bool useColors = true, bool failuresOnly = false )
  {
    Console.WriteLine(CreateJson(summary, verbose, failuresOnly));
  }

  public async Task SaveToFileAsync( TestRunSummary summary, string filePath, bool verbose = false, bool failuresOnly = false )
  {
    await File.WriteAllTextAsync(filePath, CreateJson(summary, verbose, failuresOnly));
  }

  private static string CreateJson( TestRunSummary summary, bool verbose, bool failuresOnly )
  {
    var output = CreateOutput(summary, verbose, failuresOnly);
    return JsonSerializer.Serialize(output, JsonOptions);
  }

  private static object CreateOutput( TestRunSummary summary, bool verbose, bool failuresOnly )
  {
    var failures = summary.Results
      .Where(r => r.Status == TestStatus.Failed)
      .Select(r => CreateResult(r, verbose))
      .ToList();

    var displayResults = failuresOnly
      ? summary.Results.Where(r => r.Status == TestStatus.Failed)
      : summary.Results;

    return new {
      schemaVersion = "resty.agent.v1",
      status = summary.HasFailures ? "failed" : "passed",
      exitCode = summary.HasFailures ? 1 : 0,
      nextAction = summary.HasFailures ? "inspect_failures" : "none",
      summary = new {
        total = summary.TotalTests,
        passed = summary.PassedTests,
        failed = summary.FailedTests,
        skipped = summary.SkippedTests,
        passRate = summary.PassRate,
        durationMs = (long)Math.Round(summary.TotalDuration.TotalMilliseconds),
        startTime = summary.StartTime,
        endTime = summary.EndTime
      },
      failures,
      results = displayResults.Select(r => CreateResult(r, verbose)).ToList(),
      metadata = new {
        tool = "Resty",
        version = GetVersion(),
        timestamp = DateTime.UtcNow,
        mode = "agent"
      }
    };
  }

  private static object CreateResult( TestResult result, bool verbose )
  {
    var responsePreview = CreatePreview(result.ResponseBody, DefaultPreviewLength);

    var core = new Dictionary<string, object?> {
      ["test"] = result.Test.Name,
      ["description"] = result.Test.Description,
      ["file"] = result.Test.SourceFile,
      ["line"] = result.Test.SourceLine,
      ["method"] = result.Test.Method,
      ["url"] = result.RequestInfo?.Url ?? result.Test.Url,
      ["status"] = result.Status.ToString().ToLowerInvariant(),
      ["skipReason"] = result.SkipReason?.ToString().ToLowerInvariant(),
      ["durationMs"] = (long)Math.Round(result.Duration.TotalMilliseconds),
      ["statusCode"] = result.StatusCode.HasValue ? (int)result.StatusCode.Value : null,
      ["error"] = result.ErrorMessage,
      ["responsePreview"] = responsePreview.Value,
      ["responseTruncated"] = responsePreview.Truncated
    };

    if (result.ExtractedVariables.Count > 0) {
      core["extractedVariables"] = result.ExtractedVariables;
    }

    if (verbose) {
      core["request"] = new {
        headers = result.RequestInfo?.Headers ?? new Dictionary<string, string>(),
        body = result.RequestInfo?.Body,
        contentType = result.RequestInfo?.ContentType
      };
      core["response"] = new {
        headers = result.ResponseHeaders,
        body = result.ResponseBody
      };

      if (result.VariableSnapshot.Count > 0) {
        core["variableSnapshot"] = result.VariableSnapshot.ToDictionary(
          kvp => kvp.Key,
          kvp => new { value = kvp.Value.Value, source = kvp.Value.Source }
        );
      }
    }

    return core;
  }

  private static (string? Value, bool Truncated) CreatePreview( string? value, int maxLength )
  {
    if (string.IsNullOrEmpty(value) || value.Length <= maxLength) {
      return (value, false);
    }

    return (value[..maxLength], true);
  }

  private static string GetVersion()
  {
    var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
    return assembly.GetName().Version?.ToString() ?? "1.0.0";
  }
}
