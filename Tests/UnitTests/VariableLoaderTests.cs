namespace Resty.Tests;

using Resty.Core.Variables;

public class VariableLoaderTests
{
  [Fact]
  public void LoadIncludedVariables_ShouldLoadSimpleFile()
  {
    // Arrange
    var tempDir = Path.GetTempPath();
    var varFile = Path.Combine(tempDir, "test_vars.yaml");

    File.WriteAllText(varFile, """
      variables:
        host: localhost:5114
        username: admin
      """);

    try {
      // Act
      var variables = VariableLoader.LoadIncludedVariables([Path.GetFileName(varFile)], tempDir);

      // Assert
      Assert.Equal(2, variables.Count);
      Assert.Equal("localhost:5114", variables["host"]);
      Assert.Equal("admin", variables["username"]);
    } finally {
      File.Delete(varFile);
    }
  }

  [Fact]
  public void LoadIncludedVariables_ShouldHandleNestedIncludes()
  {
    // Arrange
    var tempDir = Path.GetTempPath();
    var baseFile = Path.Combine(tempDir, "base.yaml");
    var sharedFile = Path.Combine(tempDir, "shared.yaml");
    var privateFile = Path.Combine(tempDir, "private.yaml");

    // Create nested include chain: base -> shared -> private
    File.WriteAllText(privateFile, """
      variables:
        secret_key: super_secret
        timeout: 10
      """);

    File.WriteAllText(sharedFile, """
      include:
        - private.yaml
      variables:
        host: localhost
        timeout: 30
      """);

    File.WriteAllText(baseFile, """
      include:
        - shared.yaml
      variables:
        username: admin
      """);

    try {
      // Act
      var variables = VariableLoader.LoadIncludedVariables([Path.GetFileName(baseFile)], tempDir);

      // Assert
      Assert.Equal(4, variables.Count);
      Assert.Equal("super_secret", variables["secret_key"]);  // From private
      Assert.Equal("localhost", variables["host"]);          // From shared
      Assert.Equal("admin", variables["username"]);          // From base
      Assert.Equal("admin", variables["username"]);          // Base should override
      Assert.Equal(30, Convert.ToInt32(variables["timeout"]));  // shared.yaml should override private.yaml
    } finally {
      File.Delete(baseFile);
      File.Delete(sharedFile);
      File.Delete(privateFile);
    }
  }

  [Fact]
  public void LoadIncludedVariables_ShouldDetectCircularIncludes()
  {
    // Arrange
    var tempDir = Path.GetTempPath();
    var file1 = Path.Combine(tempDir, "file1.yaml");
    var file2 = Path.Combine(tempDir, "file2.yaml");

    File.WriteAllText(file1, """
      include:
        - file2.yaml
      variables:
        var1: value1
      """);

    File.WriteAllText(file2, """
      include:
        - file1.yaml
      variables:
        var2: value2
      """);

    try {
      // Act & Assert
      var ex = Assert.Throws<InvalidOperationException>(() =>
          VariableLoader.LoadIncludedVariables([Path.GetFileName(file1)], tempDir));

      Assert.Contains("Circular include detected", ex.Message);
    } finally {
      File.Delete(file1);
      File.Delete(file2);
    }
  }

  [Fact]
  public void LoadIncludedVariables_ShouldThrowForMissingFile()
  {
    // Arrange
    var tempDir = Path.GetTempPath();

    // Act & Assert
    var ex = Assert.Throws<FileNotFoundException>(() =>
        VariableLoader.LoadIncludedVariables(["nonexistent.yaml"], tempDir));

    Assert.Contains("Include file not found", ex.Message);
    Assert.Contains("nonexistent.yaml", ex.Message);
  }

  [Fact]
  public void LoadIncludedVariables_ShouldHandleRawDictionaryFiles()
  {
    // Arrange - File without explicit 'variables' key
    var tempDir = Path.GetTempPath();
    var varFile = Path.Combine(tempDir, "raw_vars.yaml");

    File.WriteAllText(varFile, """
      host: localhost:5114
      username: admin
      timeout: 30
      """);

    try {
      // Act
      var variables = VariableLoader.LoadIncludedVariables([Path.GetFileName(varFile)], tempDir);

      // Assert
      Assert.Equal(3, variables.Count);
      Assert.Equal("localhost:5114", variables["host"]);
      Assert.Equal("admin", variables["username"]);
      Assert.Equal(30, Convert.ToInt32(variables["timeout"]));
    } finally {
      File.Delete(varFile);
    }
  }

  [Fact]
  public void LoadIncludedVariables_ShouldMaintainIncludeOrder()
  {
    // Arrange - Test that later files override earlier ones
    var tempDir = Path.GetTempPath();
    var file1 = Path.Combine(tempDir, "vars1.yaml");
    var file2 = Path.Combine(tempDir, "vars2.yaml");

    File.WriteAllText(file1, """
      variables:
        host: first.example.com
        var1: from_first
      """);

    File.WriteAllText(file2, """
      variables:
        host: second.example.com
        var2: from_second
      """);

    try {
      // Act - Include in specific order
      var variables = VariableLoader.LoadIncludedVariables([
          Path.GetFileName(file1),
                Path.GetFileName(file2)
      ], tempDir);

      // Assert - Later file should override
      Assert.Equal(3, variables.Count);
      Assert.Equal("second.example.com", variables["host"]);  // file2 overrides file1
      Assert.Equal("from_first", variables["var1"]);
      Assert.Equal("from_second", variables["var2"]);
    } finally {
      File.Delete(file1);
      File.Delete(file2);
    }
  }

  [Fact]
  public void LoadIncludedVariables_ShouldHandleAbsolutePaths()
  {
    // Arrange
    var tempDir = Path.GetTempPath();
    var varFile = Path.Combine(tempDir, "absolute_test.yaml");

    File.WriteAllText(varFile, """
      variables:
        test_var: absolute_path_worked
      """);

    try {
      // Act - Use absolute path
      var variables = VariableLoader.LoadIncludedVariables([varFile], tempDir);

      // Assert
      Assert.Single(variables);
      Assert.Equal("absolute_path_worked", variables["test_var"]);
    } finally {
      File.Delete(varFile);
    }
  }
}
