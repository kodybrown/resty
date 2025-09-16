namespace Resty.Core.Execution;

using Resty.Core.Models;
using Resty.Core.Parsers;
using Resty.Core.Variables;

/// <summary>
/// Orchestrates the execution of test suites with proper variable state management.
/// </summary>
public class TestSuiteRunner
{
  private readonly HttpTestExecutor _executor;
  private readonly int? _optTimeout;
  private readonly bool _optTimeoutWasSet;

  public TestSuiteRunner( HttpTestExecutor executor, int? optTimeout = null, bool optTimeoutWasSet = false )
  {
    _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    _optTimeout = optTimeout;
    _optTimeoutWasSet = optTimeoutWasSet;
  }

  /// <summary>
  /// Executes all tests in a test suite with proper variable state progression.
  /// </summary>
  /// <param name="testSuite">The test suite to execute.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>List of test results in execution order.</returns>
  public async Task<List<TestResult>> RunTestSuiteAsync(
      TestSuite testSuite,
      CancellationToken cancellationToken = default )
  {
    var results = new List<TestResult>();
    var variableStore = new VariableStore();

    // Step 1: Load included variables (lowest precedence)
    if (testSuite.IncludeFiles.Count > 0) {
      try {
        var includedVariables = VariableLoader.LoadIncludedVariables(testSuite.IncludeFiles, testSuite.Directory);
        variableStore.SetIncludedVariables(includedVariables);
      } catch (Exception ex) {
        // Create a failure result for include loading
        var failureResult = CreateIncludeFailureResult(testSuite, ex);
        results.Add(failureResult);
        return results; // Cannot proceed without variables
      }
    }

    // Step 2: Apply file-level variables from the test suite
    variableStore.UpdateFileVariables(testSuite.Variables);

    // Step 3: Re-parse the file to get YAML blocks in execution order
    var yamlBlocks = MarkdownParser.FindYamlBlocks(testSuite.FilePath);
    var sortedBlocks = yamlBlocks.OrderBy(kvp => kvp.Key).ToList();

    // Step 4: Execute tests in file order, maintaining variable state
    foreach (var (lineNumber, yamlBlock) in sortedBlocks) {
      // Update variables from this block (if any)
      if (yamlBlock.Variables != null) {
        variableStore.UpdateFileVariables(yamlBlock.Variables);
      }

      // Execute test if this block defines one
      if (yamlBlock.IsTest) {
        var httpTest = HttpTest.FromYamlBlock(yamlBlock, testSuite.FilePath, lineNumber);

        // Check if test is disabled
        if (yamlBlock.Disabled) {
          var variableSnapshot = variableStore.GetVariableSnapshot();
          var skippedResult = TestResult.Skipped(httpTest, "Test is disabled", variableSnapshot);
          results.Add(skippedResult);
          continue;
        }

        // Execute the test with retry logic and timeout parameters
        var result = await _executor.ExecuteTestAsync(
          httpTest,
          variableStore,
          yamlBlock.Retry,
          _optTimeout,
          _optTimeoutWasSet,
          cancellationToken);
        results.Add(result);

        // Capture response variables for successful tests
        if (result.Passed && result.ExtractedVariables.Count > 0) {
          variableStore.SetCapturedVariables(result.ExtractedVariables);
        }
      }
    }

    return results;
  }

  /// <summary>
  /// Executes tests from multiple test suite files.
  /// </summary>
  /// <param name="filePaths">Paths to Markdown test files.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>Aggregated test results from all files.</returns>
  public async Task<TestRunSummary> RunTestFilesAsync(
      IEnumerable<string> filePaths,
      CancellationToken cancellationToken = default )
  {
    var startTime = DateTime.UtcNow;
    var allResults = new List<TestResult>();
    var testSuites = new List<TestSuite>();

    foreach (var filePath in filePaths) {
      try {
        // Parse the test file
        var testSuite = MarkdownParser.ParseTestSuite(filePath);
        testSuites.Add(testSuite);

        // Skip files with no tests
        if (!testSuite.HasTests) {
          continue;
        }

        // Execute the test suite
        var suiteResults = await RunTestSuiteAsync(testSuite, cancellationToken);
        allResults.AddRange(suiteResults);
      } catch (Exception ex) {
        // Create a failure result for file parsing errors
        var failureResult = CreateFileFailureResult(filePath, ex);
        allResults.Add(failureResult);
      }
    }

    var endTime = DateTime.UtcNow;
    return TestRunSummary.Create(startTime, endTime, allResults, testSuites);
  }

  /// <summary>
  /// Filters and executes tests based on criteria.
  /// </summary>
  /// <param name="filePaths">Paths to test files.</param>
  /// <param name="testNameFilter">Exact test names to run (optional).</param>
  /// <param name="testPatternFilter">Pattern to match in test names (optional).</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>Test run summary with filtered results.</returns>
  public async Task<TestRunSummary> RunFilteredTestsAsync(
      IEnumerable<string> filePaths,
      IEnumerable<string>? testNameFilter = null,
      IEnumerable<string>? testPatternFilter = null,
      CancellationToken cancellationToken = default )
  {
    var startTime = DateTime.UtcNow;
    var allResults = new List<TestResult>();
    var testSuites = new List<TestSuite>();

    foreach (var filePath in filePaths) {
      try {
        // Parse and filter the test suite
        var testSuite = MarkdownParser.ParseTestSuite(filePath);

        // Apply filters
        if (testNameFilter != null) {
          testSuite = testSuite.FilterByTestNames(testNameFilter);
        }

        if (testPatternFilter != null) {
          testSuite = testSuite.FilterByPatterns(testPatternFilter);
        }

        testSuites.Add(testSuite);

        // Skip if no tests match the filter
        if (!testSuite.HasTests) {
          continue;
        }

        // Execute the filtered test suite
        var suiteResults = await RunTestSuiteAsync(testSuite, cancellationToken);
        allResults.AddRange(suiteResults);
      } catch (Exception ex) {
        var failureResult = CreateFileFailureResult(filePath, ex);
        allResults.Add(failureResult);
      }
    }

    var endTime = DateTime.UtcNow;
    return TestRunSummary.Create(startTime, endTime, allResults, testSuites);
  }

  /// <summary>
  /// Lists all available tests without executing them.
  /// </summary>
  /// <param name="filePaths">Paths to test files.</param>
  /// <returns>List of discovered tests.</returns>
  public List<HttpTest> ListAvailableTests( IEnumerable<string> filePaths )
  {
    var allTests = new List<HttpTest>();

    foreach (var filePath in filePaths) {
      try {
        var testSuite = MarkdownParser.ParseTestSuite(filePath);
        allTests.AddRange(testSuite.Tests);
      } catch {
        // Skip files that can't be parsed for listing
      }
    }

    return allTests;
  }

  /// <summary>
  /// Creates a failure result for include file loading errors.
  /// </summary>
  private TestResult CreateIncludeFailureResult( TestSuite testSuite, Exception ex )
  {
    var dummyTest = new HttpTest {
      Name = "Include Loading",
      Method = "NONE",
      Url = "file://include",
      SourceFile = testSuite.FilePath,
      SourceLine = 1
    };

    var now = DateTime.UtcNow;
    return TestResult.Failure(dummyTest, now, now, $"Failed to load include files: {ex.Message}", ex);
  }

  /// <summary>
  /// Creates a failure result for file parsing errors.
  /// </summary>
  private TestResult CreateFileFailureResult( string filePath, Exception ex )
  {
    var dummyTest = new HttpTest {
      Name = "File Parsing",
      Method = "NONE",
      Url = "file://parse",
      SourceFile = filePath,
      SourceLine = 1
    };

    var now = DateTime.UtcNow;
    return TestResult.Failure(dummyTest, now, now, $"Failed to parse test file: {ex.Message}", ex);
  }
}
