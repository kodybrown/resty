namespace Resty.Core.Models;

/// <summary>
/// Summary of a complete test run across multiple test suites.
/// </summary>
public record TestRunSummary
{
  /// <summary>
  /// Time when the test run started.
  /// </summary>
  public DateTime StartTime { get; init; }

  /// <summary>
  /// Time when the test run completed.
  /// </summary>
  public DateTime EndTime { get; init; }

  /// <summary>
  /// Total duration of the test run.
  /// </summary>
  public TimeSpan TotalDuration => EndTime - StartTime;

  /// <summary>
  /// All test results from the run.
  /// </summary>
  public List<TestResult> Results { get; init; } = new();

  /// <summary>
  /// Test suites that were processed.
  /// </summary>
  public List<TestSuite> TestSuites { get; init; } = new();

  /// <summary>
  /// Total number of tests executed.
  /// </summary>
  public int TotalTests => Results.Count;

  /// <summary>
  /// Number of tests that passed.
  /// </summary>
  public int PassedTests => Results.Count(r => r.Passed);

  /// <summary>
  /// Number of tests that failed.
  /// </summary>
  public int FailedTests => Results.Count(r => r.Failed);

  /// <summary>
  /// Number of tests that were skipped.
  /// </summary>
  public int SkippedTests => Results.Count(r => r.Status == TestStatus.Skipped);

  /// <summary>
  /// Pass rate as a percentage (0-100).
  /// </summary>
  public double PassRate => TotalTests == 0 ? 0 : (PassedTests * 100.0) / TotalTests;

  /// <summary>
  /// Determines if all tests passed.
  /// </summary>
  public bool AllPassed => TotalTests > 0 && FailedTests == 0;

  /// <summary>
  /// Determines if any tests failed.
  /// </summary>
  public bool HasFailures => FailedTests > 0;

  /// <summary>
  /// Gets results grouped by test suite.
  /// </summary>
  public Dictionary<string, List<TestResult>> ResultsByFile
    => Results.GroupBy(r => r.Test.SourceFile)
              .ToDictionary(g => g.Key, g => g.ToList());

  /// <summary>
  /// Gets the slowest tests sorted by duration.
  /// </summary>
  /// <param name="count">Number of slowest tests to return.</param>
  public List<TestResult> GetSlowestTests( int count = 10 )
    => Results.OrderByDescending(r => r.Duration).Take(count).ToList();

  /// <summary>
  /// Gets all failed tests.
  /// </summary>
  public List<TestResult> GetFailedTests()
    => Results.Where(r => r.Failed).ToList();

  /// <summary>
  /// Creates a summary from a collection of test results.
  /// </summary>
  public static TestRunSummary Create(
      DateTime startTime,
      DateTime endTime,
      IEnumerable<TestResult> results,
      IEnumerable<TestSuite> testSuites )
  {
    return new TestRunSummary {
      StartTime = startTime,
      EndTime = endTime,
      Results = results.ToList(),
      TestSuites = testSuites.ToList()
    };
  }
}
