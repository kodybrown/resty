namespace Resty.Core.Execution;

using Resty.Core.Exceptions;
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
        var includeYaml = testSuite.IncludeFiles
          .Where(f => f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
          .ToList();
        if (includeYaml.Count > 0) {
          var includedVariables = VariableLoader.LoadIncludedVariables(includeYaml, testSuite.Directory);
          variableStore.SetIncludedVariables(includedVariables);
        }
      } catch (Exception ex) {
        // Create a failure result for include loading
        var failureResult = CreateIncludeFailureResult(testSuite, ex);
        results.Add(failureResult);
        return results; // Cannot proceed without variables
      }
    }

    // Step 2: Apply file-level variables from the test suite
    variableStore.UpdateFileVariables(testSuite.Variables);

    // Step 3: Re-parse the file to get YAML blocks and resolve dependencies
    var yamlBlocks = MarkdownParser.FindYamlBlocks(testSuite.FilePath);

    // Track source info (file, line) for all blocks across this file and any included .rest/.resty files
    var blockSourceMap = new Dictionary<YamlBlock, (string FilePath, int Line)>();
    foreach (var (line, block) in yamlBlocks) {
      blockSourceMap[block] = (testSuite.FilePath, line);
    }

    var allBlocks = yamlBlocks.Values.ToList();

    // Step 3.1: Load included .rest/.resty files to enable cross-file dependencies
    var includeResty = testSuite.IncludeFiles
      .Where(f => f.EndsWith(".rest", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".resty", StringComparison.OrdinalIgnoreCase))
      .ToList();

    foreach (var inc in includeResty) {
      var fullPath = Path.IsPathRooted(inc) ? inc : Path.GetFullPath(Path.Combine(testSuite.Directory, inc));
      if (!File.Exists(fullPath)) {
        // Skip silently; dependency resolver will surface missing tests if referenced
        continue;
      }
      var incBlocks = MarkdownParser.FindYamlBlocks(fullPath);
      foreach (var (line, block) in incBlocks) {
        allBlocks.Add(block);
        blockSourceMap[block] = (fullPath, line);
      }
    }

    // Determine selected tests: only tests defined in this file should be primary selections; dependencies can come from includes
    var ownTestNames = yamlBlocks.Values.Where(b => b.IsTest && !string.IsNullOrWhiteSpace(b.Test)).Select(b => b.Test!).Distinct().ToList();

    IReadOnlyList<YamlBlock> resolvedBlocks;
    try {
      var dependencyResolver = new DependencyResolver();
      resolvedBlocks = dependencyResolver.Resolve(allBlocks, ownTestNames);
    } catch (MissingDependencyException) {
      throw; // Re-throw to be caught at Program.cs level
    } catch (CircularDependencyException) {
      throw; // Re-throw to be caught at Program.cs level
    }

    // Step 4: Execute tests in dependency-resolved order, maintaining variable state
    foreach (var yamlBlock in resolvedBlocks) {
      // Update variables from this block (if any)
      if (yamlBlock.Variables != null) {
        variableStore.UpdateFileVariables(yamlBlock.Variables);
      }

      // Execute test if this block defines one
      if (yamlBlock.IsTest) {
        // Find the original file and line number for this block
        if (!blockSourceMap.TryGetValue(yamlBlock, out var src)) {
          src = (testSuite.FilePath, 1);
        }
        var httpTest = HttpTest.FromYamlBlock(yamlBlock, src.FilePath, src.Line);

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
  /// Executes filtered tests from a single test suite file using dependency resolution.
  /// </summary>
  /// <param name="filePath">Path to the test file.</param>
  /// <param name="testNameFilter">Exact test names to run (optional).</param>
  /// <param name="testPatternFilter">Pattern to match in test names (optional).</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>List of test results.</returns>
  private async Task<List<TestResult>> RunFilteredTestSuiteAsync(
      string filePath,
      IEnumerable<string>? testNameFilter = null,
      IEnumerable<string>? testPatternFilter = null,
      CancellationToken cancellationToken = default )
  {
    var results = new List<TestResult>();
    var variableStore = new VariableStore();

    // Parse the test file
    var testSuite = MarkdownParser.ParseTestSuite(filePath);

    // Step 1: Load included variables (lowest precedence)
    if (testSuite.IncludeFiles.Count > 0) {
      try {
        var includeYaml = testSuite.IncludeFiles
          .Where(f => f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
          .ToList();
        if (includeYaml.Count > 0) {
          var includedVariables = VariableLoader.LoadIncludedVariables(includeYaml, testSuite.Directory);
          variableStore.SetIncludedVariables(includedVariables);
        }
      } catch (Exception ex) {
        var failureResult = CreateIncludeFailureResult(testSuite, ex);
        results.Add(failureResult);
        return results;
      }
    }

    // Step 2: Apply file-level variables from the test suite
    variableStore.UpdateFileVariables(testSuite.Variables);

    // Step 3: Get all YAML blocks and determine which tests to run
    var yamlBlocks = MarkdownParser.FindYamlBlocks(filePath);

    // Track source info (file, line) for this file and any included .rest/.resty files
    var blockSourceMap = new Dictionary<YamlBlock, (string FilePath, int Line)>();
    foreach (var (line, block) in yamlBlocks) {
      blockSourceMap[block] = (filePath, line);
    }

    var allBlocks = yamlBlocks.Values.ToList();

    // Load included .rest/.resty files so their tests can be resolved as dependencies
    var includeResty = testSuite.IncludeFiles
      .Where(f => f.EndsWith(".rest", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".resty", StringComparison.OrdinalIgnoreCase))
      .ToList();

    foreach (var inc in includeResty) {
      var fullPath = Path.IsPathRooted(inc) ? inc : Path.GetFullPath(Path.Combine(testSuite.Directory, inc));
      if (!File.Exists(fullPath)) {
        continue;
      }
      var incBlocks = MarkdownParser.FindYamlBlocks(fullPath);
      foreach (var (line, block) in incBlocks) {
        allBlocks.Add(block);
        blockSourceMap[block] = (fullPath, line);
      }
    }

    var selectedTestNames = new List<string>();

    // Apply exact name filters first
    if (testNameFilter?.Any() == true) {
      selectedTestNames.AddRange(testNameFilter);
    }

    // Apply pattern filters
    if (testPatternFilter?.Any() == true) {
      foreach (var block in yamlBlocks.Values.Where(b => b.IsTest && !string.IsNullOrWhiteSpace(b.Test))) {
        foreach (var pattern in testPatternFilter) {
          if (block.Test!.Contains(pattern, StringComparison.OrdinalIgnoreCase)) {
            selectedTestNames.Add(block.Test);
            break;
          }
        }
      }
    }

    // Use dependency resolver with selected test names
    IReadOnlyList<YamlBlock> resolvedBlocks;
    try {
      var dependencyResolver = new DependencyResolver();
      var distinctSelected = selectedTestNames.Distinct();
      resolvedBlocks = dependencyResolver.Resolve(allBlocks, distinctSelected);
    } catch (MissingDependencyException) {
      throw; // Re-throw to be caught at Program.cs level
    } catch (CircularDependencyException) {
      throw; // Re-throw to be caught at Program.cs level
    }

    // Step 4: Execute tests in dependency-resolved order
    foreach (var yamlBlock in resolvedBlocks) {
      // Update variables from this block (if any)
      if (yamlBlock.Variables != null) {
        variableStore.UpdateFileVariables(yamlBlock.Variables);
      }

      // Execute test if this block defines one
      if (yamlBlock.IsTest) {
        // Find the original file and line number for this block
        if (!blockSourceMap.TryGetValue(yamlBlock, out var src)) {
          src = (filePath, 1);
        }
        var httpTest = HttpTest.FromYamlBlock(yamlBlock, src.FilePath, src.Line);

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
      } catch (MissingDependencyException) {
        throw; // Let dependency exceptions bubble up to Program.cs
      } catch (CircularDependencyException) {
        throw; // Let dependency exceptions bubble up to Program.cs
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
        // Execute filtered tests using dependency resolution
        var suiteResults = await RunFilteredTestSuiteAsync(filePath, testNameFilter, testPatternFilter, cancellationToken);
        allResults.AddRange(suiteResults);

        // Add test suite for tracking
        var testSuite = MarkdownParser.ParseTestSuite(filePath);
        testSuites.Add(testSuite);
      } catch (MissingDependencyException) {
        throw; // Let dependency exceptions bubble up to Program.cs
      } catch (CircularDependencyException) {
        throw; // Let dependency exceptions bubble up to Program.cs
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
