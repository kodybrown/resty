namespace Resty.Core.Output;

using Resty.Core.Models;

/// <summary>
/// Interface for formatting and outputting test results.
/// </summary>
public interface IOutputFormatter
{
  /// <summary>
  /// Formats and writes the test results to the console.
  /// </summary>
  /// <param name="summary">The test run summary to format.</param>
  /// <param name="verbose">Whether to include verbose output.</param>
  void FormatAndWrite( TestRunSummary summary, bool verbose = false );

  /// <summary>
  /// Saves the test results to a file.
  /// </summary>
  /// <param name="summary">The test run summary to save.</param>
  /// <param name="filePath">The file path to save to.</param>
  /// <returns>A task representing the asynchronous save operation.</returns>
  Task SaveAsync( TestRunSummary summary, string filePath );
}
