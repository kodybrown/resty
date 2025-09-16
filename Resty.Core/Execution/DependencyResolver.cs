namespace Resty.Core.Execution;

using Resty.Core.Exceptions;
using Resty.Core.Models;

/// <summary>
/// Resolves test dependencies and determines the correct execution order.
/// Handles dependency validation, circular dependency detection, and topological sorting.
/// </summary>
public class DependencyResolver
{
  /// <summary>
  /// Resolves the execution order for tests based on their dependencies.
  /// </summary>
  /// <param name="allBlocks">All YAML blocks from the test file (including variables, includes, and tests).</param>
  /// <param name="selectedTestNames">Optional list of specific test names to run. If provided, only these tests and their dependencies will be included.</param>
  /// <returns>List of YAML blocks in the correct execution order.</returns>
  /// <exception cref="MissingDependencyException">Thrown when a test requires a dependency that doesn't exist.</exception>
  /// <exception cref="CircularDependencyException">Thrown when circular dependencies are detected.</exception>
  public IReadOnlyList<YamlBlock> Resolve( IEnumerable<YamlBlock> allBlocks, IEnumerable<string>? selectedTestNames = null )
  {
    var blockList = allBlocks.ToList();
    var testBlocks = blockList.Where(b => b.IsTest).ToList();
    var nonTestBlocks = blockList.Where(b => !b.IsTest).ToList();

    // Build dictionary of test name -> YamlBlock for quick lookup
    var testLookup = new Dictionary<string, YamlBlock>();
    foreach (var block in testBlocks) {
      if (!string.IsNullOrWhiteSpace(block.Test)) {
        testLookup[block.Test] = block;
      }
    }

    // Validate that all dependencies exist
    ValidateAllDependencies(testLookup);

    // Determine which tests to run
    var testsToRun = new HashSet<string>();
    if (selectedTestNames?.Any() == true) {
      // If specific tests are requested, include them and their transitive dependencies
      foreach (var testName in selectedTestNames) {
        if (!testLookup.ContainsKey(testName)) {
          throw new MissingDependencyException("(selection)", testName);
        }
        CollectTransitiveDependencies(testName, testLookup, testsToRun);
      }
    } else {
      // If no specific tests requested, run all tests
      testsToRun.UnionWith(testLookup.Keys);
    }

    // Check for circular dependencies
    DetectCircularDependencies(testsToRun, testLookup);

    // Perform topological sort to determine execution order
    var executionOrder = TopologicalSort(testsToRun, testLookup);

    // Build final execution list: variables/includes first, then sorted test blocks
    var result = new List<YamlBlock>();
    result.AddRange(nonTestBlocks); // Add all variable/include blocks first

    foreach (var testName in executionOrder) {
      result.Add(testLookup[testName]);
    }

    return result;
  }

  /// <summary>
  /// Validates that all test dependencies exist.
  /// </summary>
  private void ValidateAllDependencies( Dictionary<string, YamlBlock> testLookup )
  {
    foreach (var (testName, block) in testLookup) {
      if (block.Requires != null) {
        foreach (var requiredTest in block.Requires) {
          if (!testLookup.ContainsKey(requiredTest)) {
            throw new MissingDependencyException(testName, requiredTest);
          }
        }
      }
    }
  }

  /// <summary>
  /// Collects all transitive dependencies for a given test.
  /// </summary>
  private void CollectTransitiveDependencies( string testName, Dictionary<string, YamlBlock> testLookup, HashSet<string> collected )
  {
    if (collected.Contains(testName)) {
      return; // Already processed
    }

    collected.Add(testName);

    if (testLookup.TryGetValue(testName, out var block) && block.Requires != null) {
      foreach (var dependency in block.Requires) {
        CollectTransitiveDependencies(dependency, testLookup, collected);
      }
    }
  }

  /// <summary>
  /// Detects circular dependencies using depth-first search.
  /// </summary>
  private void DetectCircularDependencies( HashSet<string> testsToRun, Dictionary<string, YamlBlock> testLookup )
  {
    var visited = new HashSet<string>();
    var recursionStack = new HashSet<string>();
    var path = new List<string>();

    foreach (var testName in testsToRun) {
      if (!visited.Contains(testName)) {
        if (HasCircularDependency(testName, testLookup, visited, recursionStack, path)) {
          return; // Exception already thrown in HasCircularDependency
        }
      }
    }
  }

  /// <summary>
  /// Recursive helper for circular dependency detection.
  /// </summary>
  private bool HasCircularDependency(
    string testName,
    Dictionary<string, YamlBlock> testLookup,
    HashSet<string> visited,
    HashSet<string> recursionStack,
    List<string> path )
  {
    visited.Add(testName);
    recursionStack.Add(testName);
    path.Add(testName);

    if (testLookup.TryGetValue(testName, out var block) && block.Requires != null) {
      foreach (var dependency in block.Requires) {
        if (recursionStack.Contains(dependency)) {
          // Found a cycle - build the cycle path
          var cycleStartIndex = path.IndexOf(dependency);
          var cycle = path.Skip(cycleStartIndex).Concat(new[] { dependency }).ToList();
          throw new CircularDependencyException(cycle);
        }

        if (!visited.Contains(dependency)) {
          if (HasCircularDependency(dependency, testLookup, visited, recursionStack, path)) {
            return true;
          }
        }
      }
    }

    recursionStack.Remove(testName);
    path.RemoveAt(path.Count - 1);
    return false;
  }

  /// <summary>
  /// Performs topological sort to determine the correct execution order.
  /// Uses Kahn's algorithm.
  /// </summary>
  private List<string> TopologicalSort( HashSet<string> testsToRun, Dictionary<string, YamlBlock> testLookup )
  {
    // Calculate in-degree for each test
    var inDegree = new Dictionary<string, int>();
    var dependencies = new Dictionary<string, List<string>>();

    foreach (var testName in testsToRun) {
      inDegree[testName] = 0;
      dependencies[testName] = new List<string>();
    }

    foreach (var testName in testsToRun) {
      if (testLookup.TryGetValue(testName, out var block) && block.Requires != null) {
        foreach (var dependency in block.Requires.Where(testsToRun.Contains)) {
          dependencies[dependency].Add(testName);
          inDegree[testName]++;
        }
      }
    }

    // Find tests with no dependencies (in-degree = 0)
    var queue = new Queue<string>();
    foreach (var testName in testsToRun.Where(t => inDegree[t] == 0)) {
      queue.Enqueue(testName);
    }

    var result = new List<string>();

    while (queue.Count > 0) {
      var current = queue.Dequeue();
      result.Add(current);

      // Reduce in-degree for dependent tests
      foreach (var dependent in dependencies[current]) {
        inDegree[dependent]--;
        if (inDegree[dependent] == 0) {
          queue.Enqueue(dependent);
        }
      }
    }

    return result;
  }
}
