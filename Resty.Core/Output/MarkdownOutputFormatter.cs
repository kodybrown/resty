namespace Resty.Core.Output;

using System.Text;
using Resty.Core.Helpers;
using Resty.Core.Models;

/// <summary>
/// Formats test results for Markdown output.
/// </summary>
public class MarkdownOutputFormatter : IOutputFormatter
{
  public void WriteToConsole( TestRunSummary summary, bool verbose = false, bool useColors = true )
  {
    var s = new StringBuilder();

    s.Append('\n');
    s.Append(CreateOutput(summary, verbose));
    s.Append('\n');

    // Strip color variables if colors are disabled or output is redirected
    if (!useColors || Console.IsOutputRedirected) {
      var plainText = ColorExtensions.StripColorVariables(s.ToString());
      Console.WriteLine(plainText);
      return;
    }

    // Write with colors
    var lines = s.ToString().Replace("\r\n", "\n").Split('\n');
    foreach (var line in lines) {
      ColorExtensions.WriteToConsoleWithColors(line);
      Console.WriteLine();
    }
  }

  public async Task SaveToFileAsync( TestRunSummary summary, string filePath, bool verbose = false )
  {
    var content = CreateOutput(summary, verbose);
    var plainText = ColorExtensions.StripColorVariables(content);
    await File.WriteAllTextAsync(filePath, plainText);
  }

  private string CreateOutput( TestRunSummary summary, bool verbose = false )
  {
    var s = new StringBuilder();

    var h1 = ConsoleColors.Heading1.ToColorVariable();
    var h2 = ConsoleColors.Heading2.ToColorVariable();
    var h3 = ConsoleColors.Heading3.ToColorVariable();

    s.Append(h1).Append("# Test Results\n")
     .Append('\n');

    // Results by file
    s.Append(h2).Append("## Per File Results\n")
     .Append('\n');

    // Group results by file for better organization
    var resultsByFile = summary.Results.GroupBy(r => r.Test.SourceFile).ToList();

    foreach (var fileGroup in resultsByFile) {
      var fileName = Path.GetFileName(fileGroup.Key);

      s.Append(h3).Append($"### File: {fileName}\n")
       .Append('\n');

      foreach (var result in fileGroup.OrderBy(r => r.Test.Name)) {
        var (statusSymbol, statusColor) = result.Status switch {
          TestStatus.Passed => ("[PASS]", ConsoleColors.Passed),
          TestStatus.Failed => ("[FAIL]", ConsoleColors.Failed),
          TestStatus.Skipped => ("[SKIP]", ConsoleColors.Skipped),
          _ => ("[????]", System.ConsoleColor.Gray)
        };

        s.Append("- ")
         .Append(statusColor.ToColorVariable())
         .Append(statusSymbol);

        if (verbose) {
          s.Append(' ').Append(result.Test.Name).Append('\n');
          if (!string.IsNullOrWhiteSpace(result.Test.Description)) {
            s.Append("  - Description: ").Append(result.Test.Description).Append("\n");
          }
          s.Append("  - Method:   '").Append(result.Test.Method).Append("'\n");
          s.Append("  - URL:      '").Append(result.RequestInfo?.Url ?? result.Test.Url).Append("'\n");
          s.Append("  - Duration: '").Append($"{result.Duration.TotalSeconds:F3}s").Append("'\n");
          if (result.StatusCode.HasValue) {
            s.Append("  - Status:   '").Append(result.StatusCode).Append("'\n");
          }
          if (result.ExtractedVariables?.Count > 0) {
            s.Append("  - Variables: \n");
            foreach (var variable in result.ExtractedVariables) {
              s.Append("    - '").Append(variable.Key).Append("': '").Append(variable.Value).Append("'\n");
            }
          }
        } else {
          s.Append(' ').Append(result.Test.Name).Append(' ')
           .Append(ConsoleColors.TimeDuration.ToColorVariable())
           .Append('(').Append($"{result.Duration.TotalSeconds:F3}s").Append(')');
          if (!string.IsNullOrWhiteSpace(result.Test.Description)) {
            s.Append(": ").Append(result.Test.Description);
          }
          s.Append('\n');
        }

        // Show error details for failed tests
        if (result.Status == TestStatus.Failed && !string.IsNullOrEmpty(result.ErrorMessage)) {
          var fileLink = CreateFileLink(result);
          s.Append($"  > **Error**: {fileLink}\n");

          // Show available variables in verbose mode for debugging
          if (verbose && result.VariableSnapshot.Count > 0) {
            s.Append($"  - Available Variables:\n");
            foreach (var (name, (value, source)) in result.VariableSnapshot.OrderBy(kvp => kvp.Key)) {
              s.Append($"    - `{name}`: `{value}`  _(from {source})_\n");
            }
          }
        }

        if (verbose) {
          s.Append('\n');
        }
      }

      s.Append('\n');
    }

    // Final summary
    s.Append(h2).Append("## Summary\n")
     .Append('\n');

    // var stats = $"{summary.PassedTests}/{summary.TotalTests} passed in '{summary.TotalDuration.TotalSeconds:F2}s'";

    if (summary.HasFailures) {
      s.Append(ConsoleColors.Error.ToColorVariable())
       .Append("**TESTS FAILED**");
    } else {
      s.Append(ConsoleColors.Success.ToColorVariable())
       .Append("**ALL TESTS PASSED**");
    }
    s.Append('\n')
     .Append('\n');

    s.Append($"Passed:   {summary.PassedTests} ({summary.PassRate:P1})\n");
    s.Append($"Failed:   {summary.FailedTests}\n");
    s.Append($"Skipped:  {summary.SkippedTests}\n");
    s.Append($"Duration: {summary.TotalDuration.TotalSeconds:F2} seconds\n");
    s.Append($"Total:    {summary.TotalTests}\n");
    // s.Append($"Result:      {stats}\n");

    return s.ToString();
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
