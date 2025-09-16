namespace Resty.Core.Output;

using System.Text.Json;
using Resty.Core.Models;

/// <summary>
/// Formats test results as JSON using strongly typed models.
/// </summary>
public class JsonOutputFormatter : IOutputFormatter
{
  private static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };

  public void FormatAndWrite( TestRunSummary summary, bool verbose = false )
  {
    var jsonOutput = ConvertToJsonModel(summary, verbose);
    var json = JsonSerializer.Serialize(jsonOutput, JsonOptions);
    Console.WriteLine(json);
  }

  public async Task SaveAsync( TestRunSummary summary, string filePath )
  {
    var jsonOutput = ConvertToJsonModel(summary, verbose: true); // Always include full details when saving
    var json = JsonSerializer.Serialize(jsonOutput, JsonOptions);
    await File.WriteAllTextAsync(filePath, json);
  }

  private JsonTestOutput ConvertToJsonModel( TestRunSummary summary, bool verbose )
  {
    return new JsonTestOutput {
      Summary = new JsonSummary {
        TotalTests = summary.TotalTests,
        PassedTests = summary.PassedTests,
        FailedTests = summary.FailedTests,
        SkippedTests = summary.SkippedTests,
        PassRate = summary.PassRate,
        Duration = summary.TotalDuration.TotalSeconds,
        StartTime = summary.StartTime,
        EndTime = summary.EndTime
      },
      Results = summary.Results.Select(r => ConvertTestResult(r, verbose)).ToList(),
      Metadata = new JsonMetadata()
    };
  }

  private JsonTestResult ConvertTestResult( TestResult result, bool verbose )
  {
    var jsonResult = new JsonTestResult {
      Test = result.Test.Name,
      File = result.Test.SourceFile,
      Method = result.Test.Method,
      Url = result.RequestInfo?.Url ?? result.Test.Url,
      Status = result.Status.ToString(),
      Duration = result.Duration.TotalSeconds,
      StatusCode = result.StatusCode.HasValue ? (int)result.StatusCode.Value : null,
      ErrorMessage = result.ErrorMessage,
      ExtractedVariables = result.ExtractedVariables?.ToDictionary(kv => kv.Key, kv => (object)kv.Value) ?? new Dictionary<string, object>()
    };

    // Include request/response details if verbose or if there was an error
    if (verbose || result.Status == TestStatus.Failed) {
      if (result.RequestInfo?.Headers?.Count > 0 || !string.IsNullOrEmpty(result.RequestInfo?.Body)) {
        jsonResult.Request = new JsonRequestInfo {
          Headers = result.RequestInfo?.Headers ?? new Dictionary<string, string>(),
          Body = result.RequestInfo?.Body
        };
      }

      if (result.ResponseHeaders?.Count > 0 || !string.IsNullOrEmpty(result.ResponseBody)) {
        jsonResult.Response = new JsonResponseInfo {
          Headers = result.ResponseHeaders ?? new Dictionary<string, string>(),
          Body = result.ResponseBody
        };
      }

      // Include variable snapshot for debugging failed tests
      if (result.Status == TestStatus.Failed && result.VariableSnapshot?.Count > 0) {
        jsonResult.VariableSnapshot = result.VariableSnapshot.ToDictionary(
          kvp => kvp.Key,
          kvp => new JsonVariableInfo { Value = kvp.Value.Value, Source = kvp.Value.Source }
        );
      }
    }

    return jsonResult;
  }
}
