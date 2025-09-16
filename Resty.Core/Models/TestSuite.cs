namespace Resty.Core.Models;

/// <summary>
/// Represents a test suite parsed from a Markdown file containing variables and tests.
/// </summary>
public record TestSuite
{
  /// <summary>
  /// Path to the Markdown file this suite was parsed from.
  /// </summary>
  public string FilePath { get; init; } = string.Empty;

  /// <summary>
  /// Variables defined within the test file.
  /// </summary>
  public Dictionary<string, object> Variables { get; init; } = new();

  /// <summary>
  /// Files to include for shared variables.
  /// </summary>
  public List<string> IncludeFiles { get; init; } = new();

  /// <summary>
  /// HTTP tests defined in the file.
  /// </summary>
  public List<HttpTest> Tests { get; init; } = new();

  /// <summary>
  /// Gets the directory containing the test suite file.
  /// Used for resolving relative include paths.
  /// </summary>
  public string Directory => Path.GetDirectoryName(FilePath) ?? string.Empty;

  /// <summary>
  /// Gets the filename without path.
  /// </summary>
  public string FileName => Path.GetFileName(FilePath);

  /// <summary>
  /// Determines if this test suite has any runnable tests.
  /// </summary>
  public bool HasTests => Tests.Count > 0;

  /// <summary>
  /// Determines if this test suite has variable definitions or includes.
  /// </summary>
  public bool HasVariables => Variables.Count > 0 || IncludeFiles.Count > 0;

  /// <summary>
  /// Filters tests by exact name match.
  /// </summary>
  /// <param name="testNames">Test names to match exactly.</param>
  /// <returns>A new TestSuite with filtered tests.</returns>
  public TestSuite FilterByTestNames( IEnumerable<string> testNames )
  {
    var nameSet = testNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var filteredTests = Tests.Where(t => nameSet.Contains(t.Name)).ToList();

    return this with { Tests = filteredTests };
  }

  /// <summary>
  /// Filters tests by pattern matching on test names.
  /// </summary>
  /// <param name="patterns">Patterns to match against test names.</param>
  /// <returns>A new TestSuite with filtered tests.</returns>
  public TestSuite FilterByPatterns( IEnumerable<string> patterns )
  {
    var filteredTests = Tests.Where(test =>
        patterns.Any(pattern =>
            test.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
        .ToList();

    return this with { Tests = filteredTests };
  }

  /// <summary>
  /// Gets all resolved include file paths relative to the test suite directory.
  /// </summary>
  /// <returns>List of absolute include file paths.</returns>
  public List<string> GetResolvedIncludePaths()
  {
    var baseDir = Directory;
    return IncludeFiles.Select(file =>
    {
      return Path.IsPathRooted(file)
        ? file
        : Path.GetFullPath(Path.Combine(baseDir, file));
    }).ToList();
  }
}
