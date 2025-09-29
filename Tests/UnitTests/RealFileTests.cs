namespace Resty.Tests;

using Resty.Core.Parsers;

public class RealFileTests
{
  [Fact]
  public void ParseAuthMdFile_ShouldWork()
  {
    // Arrange - path to your real auth.md file
    var authFilePath = @".\examples\auth.md";

    // Skip test if file doesn't exist (for CI/other developers)
    if (!File.Exists(authFilePath)) {
      Assert.True(true, "Skipping test - auth.md file not found");
      return;
    }

    // Act
    var testSuite = MarkdownParser.ParseTestSuite(authFilePath);

    // Assert
    Assert.Equal(authFilePath, testSuite.FilePath);
    Assert.True(testSuite.HasTests, "Should have at least one test");

    // Find the auth test
    var authTest = testSuite.Tests.FirstOrDefault(t => t.Name == "auth");
    Assert.NotNull(authTest);
    Assert.Equal("POST", authTest.Method);
    Assert.Contains("$host/api/auth", authTest.Url);
    Assert.Contains("$admin_username", authTest.Body!);

    // Check if it has response extractors
    Assert.True(authTest.Extractors.ContainsKey("token"), "Auth test should extract token");

    // Find the verify_token test
    var verifyTest = testSuite.Tests.FirstOrDefault(t => t.Name == "verify_token");
    if (verifyTest != null) {
      Assert.Equal("GET", verifyTest.Method);
      Assert.Contains("Bearer $token", verifyTest.Authorization!);
    }
  }

  [Fact]
  public void FindYamlBlocks_WithRealAuthFile_ShouldReturnBlocks()
  {
    // Arrange - path to your real auth.md file
    var authFilePath = @".\examples\auth.md";

    // Skip test if file doesn't exist
    if (!File.Exists(authFilePath)) {
      Assert.True(true, "Skipping test - auth.md file not found");
      return;
    }

    // Act
    var blocks = MarkdownParser.FindYamlBlocks(authFilePath);

    // Assert
    Assert.True(blocks.Count > 0, "Should find at least one YAML block");

    // Should have at least one test block
    Assert.True(blocks.Values.Any(b => b.IsTest), "Should have at least one test block");

    // Print debug info
    foreach (var (line, block) in blocks.OrderBy(kvp => kvp.Key)) {
      if (block.IsTest) {
        Assert.NotNull(block.Test);
        // Output for debugging
        Console.WriteLine($"Found test '{block.Test}' at line {line}");
      } else if (block.HasVariableData) {
        Console.WriteLine($"Found variables/include block at line {line}");
      }
    }
  }
}
