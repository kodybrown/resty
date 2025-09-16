namespace Resty.Core.Exceptions;

/// <summary>
/// Exception thrown when a test requires a dependency that doesn't exist.
/// </summary>
public class MissingDependencyException : Exception
{
  public string TestName { get; }
  public string MissingDependency { get; }

  public MissingDependencyException( string testName, string missingDependency )
    : base($"Test '{testName}' requires missing test '{missingDependency}'.")
  {
    TestName = testName;
    MissingDependency = missingDependency;
  }

  public MissingDependencyException( string testName, string missingDependency, Exception innerException )
    : base($"Test '{testName}' requires missing test '{missingDependency}'.", innerException)
  {
    TestName = testName;
    MissingDependency = missingDependency;
  }
}

/// <summary>
/// Exception thrown when circular dependencies are detected in test requirements.
/// </summary>
public class CircularDependencyException : Exception
{
  public List<string> DependencyCycle { get; }

  public CircularDependencyException( IEnumerable<string> cycle )
    : base($"Circular dependency detected: {string.Join(" → ", cycle)}")
  {
    DependencyCycle = cycle.ToList();
  }

  public CircularDependencyException( IEnumerable<string> cycle, Exception innerException )
    : base($"Circular dependency detected: {string.Join(" → ", cycle)}", innerException)
  {
    DependencyCycle = cycle.ToList();
  }
}
