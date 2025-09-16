namespace Resty.Core.Output;

using System.Text;
using Resty.Core.Helpers;
using Resty.Core.Models;

/// <summary>
/// Formats test results for console output.
/// </summary>
public class ConsoleOutputFormatter : IOutputFormatter
{
  public void FormatAndWrite( TestRunSummary summary, bool verbose = false, bool useColors = true )
  {
    var s = new StringBuilder();

    Console.WriteLine();
    WriteColored("# Test Results", ConsoleColors.Heading1, useColors);
    Console.WriteLine();
    Console.WriteLine();

    // Results by file
    WriteColored("## Results by File", ConsoleColors.Heading2, useColors);
    Console.WriteLine();
    Console.WriteLine();

    // Group results by file for better organization
    var resultsByFile = summary.Results.GroupBy(r => r.Test.SourceFile).ToList();

    foreach (var fileGroup in resultsByFile) {
      var fileName = Path.GetFileName(fileGroup.Key);
      WriteColored($"### File: {fileName}", ConsoleColors.Heading3, useColors);
      Console.WriteLine();
      Console.WriteLine();

      foreach (var result in fileGroup.OrderBy(r => r.Test.Name)) {
        var (statusIcon, statusColor) = GetStatusIconAndColor(result.Status);

        Console.Write("- ");
        WriteColored(statusIcon, statusColor, useColors);

        if (verbose) {
          Console.WriteLine($" {result.Test.Name}");
          Console.WriteLine($"  - Method:   '{result.Test.Method}'");
          Console.WriteLine($"  - URL:      '{result.RequestInfo?.Url ?? result.Test.Url}'");
          Console.WriteLine($"  - Duration: '{result.Duration.TotalSeconds:F3}s'");
          if (result.StatusCode.HasValue) {
            Console.WriteLine($"  - Status:   '{result.StatusCode}'");
          }
          if (result.ExtractedVariables?.Count > 0) {
            Console.WriteLine($"  - Variables: ");
            foreach (var variable in result.ExtractedVariables) {
              Console.WriteLine($"    - '{variable.Key}': '{variable.Value}'");
            }
          }
        } else {
          Console.Write($" {result.Test.Name} ");
          WriteColored($"({result.Duration.TotalSeconds:F3}s)", ConsoleColors.TimeDuration, useColors);
          Console.WriteLine();
        }

        // Show error details for failed tests
        if (result.Status == TestStatus.Failed && !string.IsNullOrEmpty(result.ErrorMessage)) {
          var fileLink = CreateFileLink(result);
          Console.WriteLine($"  > **Error**: {fileLink}");

          // Show available variables in verbose mode for debugging
          if (verbose && result.VariableSnapshot.Count > 0) {
            Console.WriteLine($"  - Available Variables:");
            foreach (var (name, (value, source)) in result.VariableSnapshot.OrderBy(kvp => kvp.Key)) {
              Console.WriteLine($"    - `{name}`: `{value}`  _(from {source})_");
            }
          }
        }

        if (verbose) {
          Console.WriteLine();
        }
      }

      Console.WriteLine();
    }

    // Final summary
    WriteColored("## Summary", ConsoleColors.Heading2, useColors);
    Console.WriteLine();
    Console.WriteLine();

    // var stats = $"{summary.PassedTests}/{summary.TotalTests} passed in '{summary.TotalDuration.TotalSeconds:F2}s'";

    if (summary.HasFailures) {
      WriteColored($"**TESTS FAILED**", ConsoleColors.Error, useColors);
    } else {
      WriteColored($"**ALL TESTS PASSED**", ConsoleColors.Success, useColors);
    }
    Console.WriteLine();
    Console.WriteLine();

    Console.WriteLine($"Passed:   {summary.PassedTests} ({summary.PassRate:P1})");
    Console.WriteLine($"Failed:   {summary.FailedTests}  ");
    Console.WriteLine($"Skipped:  {summary.SkippedTests}  ");
    Console.WriteLine($"Duration: {summary.TotalDuration.TotalSeconds:F2} seconds");
    Console.WriteLine($"Total:    {summary.TotalTests}  ");
    // Console.WriteLine($"Result:      {summary.PassedTests}/{summary.TotalTests} passed in '{summary.TotalDuration.TotalSeconds:F2}s'");
    Console.WriteLine();
  }

  public async Task SaveAsync( TestRunSummary summary, string filePath )
  {
    var content = FormatForSave(summary);
    await File.WriteAllTextAsync(filePath, content);
  }

  private string FormatForSave( TestRunSummary summary )
  {
    var sb = new StringBuilder();

    sb.AppendLine("=== TEST RESULTS ===");
    sb.AppendLine();
    sb.AppendLine($"Total Tests: {summary.TotalTests}");
    sb.AppendLine($"Passed: {summary.PassedTests} ({summary.PassRate:P1})");
    sb.AppendLine($"Failed: {summary.FailedTests}");
    sb.AppendLine($"Skipped: {summary.SkippedTests}");
    sb.AppendLine($"Duration: {summary.TotalDuration.TotalSeconds:F2}s");
    sb.AppendLine($"Start Time: {summary.StartTime:yyyy-MM-dd HH:mm:ss}");
    sb.AppendLine($"End Time: {summary.EndTime:yyyy-MM-dd HH:mm:ss}");
    sb.AppendLine();

    var resultsByFile = summary.Results.GroupBy(r => r.Test.SourceFile).ToList();

    foreach (var fileGroup in resultsByFile) {
      sb.AppendLine($"=== {fileGroup.Key} ===");
      sb.AppendLine();

      foreach (var result in fileGroup.OrderBy(r => r.Test.Name)) {
        sb.AppendLine($"Test: {result.Test.Name}");
        sb.AppendLine($"  Status: {result.Status}");
        sb.AppendLine($"  Method: {result.Test.Method}");
        sb.AppendLine($"  URL: {result.RequestInfo?.Url ?? result.Test.Url}");
        sb.AppendLine($"  Duration: {result.Duration.TotalSeconds:F3}s");

        if (result.StatusCode.HasValue) {
          sb.AppendLine($"  Status Code: {result.StatusCode}");
        }

        if (!string.IsNullOrEmpty(result.ErrorMessage)) {
          sb.AppendLine($"  Error: {result.ErrorMessage}");
        }

        if (result.ExtractedVariables?.Count > 0) {
          sb.AppendLine("  Extracted Variables:");
          foreach (var variable in result.ExtractedVariables) {
            sb.AppendLine($"    {variable.Key}: {variable.Value}");
          }
        }

        sb.AppendLine();
      }
    }

    sb.AppendLine("=== SUMMARY ===");
    sb.AppendLine(summary.HasFailures ? "RESULT: FAILED" : "RESULT: PASSED");
    sb.AppendLine($"Total: {summary.TotalTests}, Passed: {summary.PassedTests}, Failed: {summary.FailedTests}");

    return sb.ToString();
  }

  private static (string text, ConsoleColor color) GetStatusIconAndColor( TestStatus status )
  {
    return status switch {
      TestStatus.Passed => ("[PASS]", ConsoleColors.Passed),
      TestStatus.Failed => ("[FAIL]", ConsoleColors.Failed),
      TestStatus.Skipped => ("[SKIP]", ConsoleColors.Skipped),
      _ => ("[????]", System.ConsoleColor.Gray)
    };
  }

  private static void WriteColored( string text, System.ConsoleColor color, bool useColors )
  {
    if (!useColors || Console.IsOutputRedirected) {
      Console.Write(text);
      return;
    }

    var originalColor = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.Write(text);
    Console.ForegroundColor = originalColor;
  }

  private static string CreateFileLink( TestResult result )
  {
    try {
      var fullPath = Path.GetFullPath(result.Test.SourceFile);
      var fileUri = new Uri(fullPath).ToString();
      var errorMessage = result.ErrorMessage ?? "Unknown error";
      var lineNumber = result.Test.SourceLine;

      return $"[{errorMessage} (line {lineNumber})]({fileUri}#{lineNumber})";
    } catch {
      // Fallback if file path processing fails
      return result.ErrorMessage ?? "Unknown error";
    }
  }
}
