namespace Resty.Core.Output;

using System.Text;
using Resty.Core.Models;

/// <summary>
/// Formats test results for console output.
/// </summary>
public class ConsoleOutputFormatter : IOutputFormatter
{
  public void FormatAndWrite( TestRunSummary summary, bool verbose = false )
  {
    Console.WriteLine();
    Console.WriteLine("═══════════════════════════════════════════════════════════════════");
    Console.WriteLine("                          TEST RESULTS");
    Console.WriteLine("═══════════════════════════════════════════════════════════════════");
    Console.WriteLine();

    // Summary statistics
    Console.WriteLine($"Total Tests:    {summary.TotalTests}");
    Console.WriteLine($"Passed:         {summary.PassedTests} ({summary.PassRate:P1})");
    Console.WriteLine($"Failed:         {summary.FailedTests}");
    Console.WriteLine($"Skipped:        {summary.SkippedTests}");
    Console.WriteLine($"Duration:       {summary.TotalDuration.TotalSeconds:F2}s");
    Console.WriteLine();

    // Group results by file for better organization
    var resultsByFile = summary.Results.GroupBy(r => r.Test.SourceFile).ToList();

    foreach (var fileGroup in resultsByFile) {
      Console.WriteLine($"📁 {Path.GetFileName(fileGroup.Key)}");
      Console.WriteLine($"   {fileGroup.Key}");
      Console.WriteLine();

      foreach (var result in fileGroup.OrderBy(r => r.Test.Name)) {
        var statusIcon = GetStatusIcon(result.Status);
        var statusColor = GetStatusColor(result.Status);

        Console.Write($"  {statusIcon} ");
        WriteColored(result.Test.Name, statusColor);

        if (verbose) {
          Console.WriteLine();
          Console.WriteLine($"     Method: {result.Test.Method}");
          Console.WriteLine($"     URL: {result.RequestInfo?.Url ?? result.Test.Url}");
          Console.WriteLine($"     Duration: {result.Duration.TotalSeconds:F3}s");

          if (result.StatusCode.HasValue) {
            Console.WriteLine($"     Status Code: {result.StatusCode}");
          }

          if (result.ExtractedVariables?.Count > 0) {
            Console.WriteLine("     Extracted Variables:");
            foreach (var variable in result.ExtractedVariables) {
              Console.WriteLine($"       {variable.Key}: {variable.Value}");
            }
          }
        } else {
          Console.Write($" ({result.Duration.TotalSeconds:F3}s)");
        }

        Console.WriteLine();

        // Show error details for failed tests
        if (result.Status == TestStatus.Failed && !string.IsNullOrEmpty(result.ErrorMessage)) {
          WriteColored($"     Error: {result.ErrorMessage}", ConsoleColor.Red);
          Console.WriteLine();

          // Show available variables in verbose mode for debugging
          if (verbose && result.VariableSnapshot.Count > 0) {
            Console.WriteLine("     Available Variables:");
            foreach (var (name, (value, source)) in result.VariableSnapshot.OrderBy(kvp => kvp.Key)) {
              Console.WriteLine($"       {name}: {value} (from {source})");
            }
            Console.WriteLine();
          }
        }

        if (verbose) {
          Console.WriteLine();
        }
      }

      Console.WriteLine();
    }

    // Final summary
    Console.WriteLine("═══════════════════════════════════════════════════════════════════");

    if (summary.HasFailures) {
      WriteColored("❌ TESTS FAILED", ConsoleColor.Red);
    } else {
      WriteColored("✅ ALL TESTS PASSED", ConsoleColor.Green);
    }

    Console.WriteLine($" ({summary.PassedTests}/{summary.TotalTests} passed in {summary.TotalDuration.TotalSeconds:F2}s)");
    Console.WriteLine("═══════════════════════════════════════════════════════════════════");
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

  private static string GetStatusIcon( TestStatus status )
  {
    return status switch {
      TestStatus.Passed => "✅",
      TestStatus.Failed => "❌",
      TestStatus.Skipped => "⏭️",
      _ => "❓"
    };
  }

  private static ConsoleColor GetStatusColor( TestStatus status )
  {
    return status switch {
      TestStatus.Passed => ConsoleColor.Green,
      TestStatus.Failed => ConsoleColor.Red,
      TestStatus.Skipped => ConsoleColor.Yellow,
      _ => ConsoleColor.White
    };
  }

  private static void WriteColored( string text, ConsoleColor color )
  {
    var originalColor = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.Write(text);
    Console.ForegroundColor = originalColor;
  }
}
