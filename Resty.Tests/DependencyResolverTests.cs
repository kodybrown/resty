namespace Resty.Tests;

using Resty.Core.Exceptions;
using Resty.Core.Execution;
using Resty.Core.Models;

public class DependencyResolverTests
{
  [Fact]
  public void Resolve_WithNoDependencies_ShouldReturnTestsInOriginalOrder()
  {
    // Arrange
    var blocks = new List<YamlBlock>
    {
      new YamlBlock { Variables = new Dictionary<string, object> { ["host"] = "localhost" } },
      new YamlBlock { Test = "test1", Get = "/api/test1" },
      new YamlBlock { Test = "test2", Post = "/api/test2" }
    };

    var resolver = new DependencyResolver();

    // Act
    var result = resolver.Resolve(blocks);

    // Assert
    Assert.Equal(3, result.Count);
    Assert.True(result[0].HasVariableData); // Variables block first
    Assert.Equal("test1", result[1].Test);
    Assert.Equal("test2", result[2].Test);
  }

  [Fact]
  public void Resolve_WithSimpleDependency_ShouldOrderCorrectly()
  {
    // Arrange
    var blocks = new List<YamlBlock>
    {
      new YamlBlock { Test = "test2", Get = "/api/test2", Requires = new List<string> { "test1" } },
      new YamlBlock { Test = "test1", Post = "/api/test1" }
    };

    var resolver = new DependencyResolver();

    // Act
    var result = resolver.Resolve(blocks);

    // Assert
    Assert.Equal(2, result.Count);
    Assert.Equal("test1", result[0].Test); // Dependency first
    Assert.Equal("test2", result[1].Test); // Dependent second
  }

  [Fact]
  public void Resolve_WithChainedDependencies_ShouldOrderCorrectly()
  {
    // Arrange
    var blocks = new List<YamlBlock>
    {
      new YamlBlock { Test = "test3", Get = "/api/test3", Requires = new List<string> { "test2" } },
      new YamlBlock { Test = "test1", Post = "/api/test1" },
      new YamlBlock { Test = "test2", Put = "/api/test2", Requires = new List<string> { "test1" } }
    };

    var resolver = new DependencyResolver();

    // Act
    var result = resolver.Resolve(blocks);

    // Assert
    Assert.Equal(3, result.Count);
    Assert.Equal("test1", result[0].Test); // Root dependency
    Assert.Equal("test2", result[1].Test); // Middle dependency
    Assert.Equal("test3", result[2].Test); // Final dependent
  }

  [Fact]
  public void Resolve_WithSelectedTests_ShouldIncludeOnlySelectedAndDependencies()
  {
    // Arrange
    var blocks = new List<YamlBlock>
    {
      new YamlBlock { Test = "test1", Post = "/api/test1" },
      new YamlBlock { Test = "test2", Get = "/api/test2", Requires = new List<string> { "test1" } },
      new YamlBlock { Test = "test3", Put = "/api/test3" } // Independent test
    };

    var resolver = new DependencyResolver();

    // Act - Only select test2, which should include test1 as dependency
    var result = resolver.Resolve(blocks, new[] { "test2" });

    // Assert
    Assert.Equal(2, result.Count); // Should not include test3
    Assert.Equal("test1", result[0].Test);
    Assert.Equal("test2", result[1].Test);
  }

  [Fact]
  public void Resolve_WithMissingDependency_ShouldThrowException()
  {
    // Arrange
    var blocks = new List<YamlBlock>
    {
      new YamlBlock { Test = "test1", Post = "/api/test1", Requires = new List<string> { "nonexistent" } }
    };

    var resolver = new DependencyResolver();

    // Act & Assert
    var exception = Assert.Throws<MissingDependencyException>(() => resolver.Resolve(blocks));
    Assert.Equal("test1", exception.TestName);
    Assert.Equal("nonexistent", exception.MissingDependency);
  }

  [Fact]
  public void Resolve_WithCircularDependency_ShouldThrowException()
  {
    // Arrange
    var blocks = new List<YamlBlock>
    {
      new YamlBlock { Test = "test1", Post = "/api/test1", Requires = new List<string> { "test2" } },
      new YamlBlock { Test = "test2", Get = "/api/test2", Requires = new List<string> { "test1" } }
    };

    var resolver = new DependencyResolver();

    // Act & Assert
    var exception = Assert.Throws<CircularDependencyException>(() => resolver.Resolve(blocks));
    Assert.Contains("test1", exception.DependencyCycle);
    Assert.Contains("test2", exception.DependencyCycle);
  }

  [Fact]
  public void Resolve_WithMultipleDependencies_ShouldOrderCorrectly()
  {
    // Arrange
    var blocks = new List<YamlBlock>
    {
      new YamlBlock { Test = "final", Get = "/api/final", Requires = new List<string> { "dep1", "dep2" } },
      new YamlBlock { Test = "dep1", Post = "/api/dep1" },
      new YamlBlock { Test = "dep2", Put = "/api/dep2" }
    };

    var resolver = new DependencyResolver();

    // Act
    var result = resolver.Resolve(blocks);

    // Assert
    Assert.Equal(3, result.Count);
    Assert.Equal("final", result[2].Test); // Final test should be last

    // Dependencies should come first, but order between them can vary
    var dependencyNames = new[] { result[0].Test, result[1].Test };
    Assert.Contains("dep1", dependencyNames);
    Assert.Contains("dep2", dependencyNames);
  }

  [Fact]
  public void Resolve_WithVariablesAndIncludes_ShouldKeepThemFirst()
  {
    // Arrange
    var blocks = new List<YamlBlock>
    {
      new YamlBlock { Test = "test1", Post = "/api/test1", Requires = new List<string> { "test2" } },
      new YamlBlock { Include = new List<string> { "config.yaml" } },
      new YamlBlock { Variables = new Dictionary<string, object> { ["host"] = "localhost" } },
      new YamlBlock { Test = "test2", Get = "/api/test2" }
    };

    var resolver = new DependencyResolver();

    // Act
    var result = resolver.Resolve(blocks);

    // Assert
    Assert.Equal(4, result.Count);

    // Variables and includes should be at the beginning
    Assert.True(result[0].Include?.Count > 0);
    Assert.True(result[1].HasVariableData);

    // Tests should be ordered by dependency
    Assert.Equal("test2", result[2].Test);
    Assert.Equal("test1", result[3].Test);
  }

  [Fact]
  public void Resolve_WithSelectedNonExistentTest_ShouldThrowException()
  {
    // Arrange
    var blocks = new List<YamlBlock>
    {
      new YamlBlock { Test = "test1", Post = "/api/test1" }
    };

    var resolver = new DependencyResolver();

    // Act & Assert
    var exception = Assert.Throws<MissingDependencyException>(() => resolver.Resolve(blocks, new[] { "nonexistent" }));
    Assert.Equal("(selection)", exception.TestName);
    Assert.Equal("nonexistent", exception.MissingDependency);
  }
}
