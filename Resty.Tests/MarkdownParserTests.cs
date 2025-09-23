namespace Resty.Tests;

using Resty.Core.Parsers;
using Resty.Core.Models;

public class MarkdownParserTests
{
  [Fact]
  public void FindYamlBlocks_ShouldParseSimpleVariablesBlock()
  {
    // Arrange
    var markdown = """
# Test File

Some documentation here.

```yaml
variables:
  host: http://localhost:5114
  username: admin
```

More documentation.
""";

    // Act
    var blocks = MarkdownParser.FindYamlBlocks(markdown, "test.md");

    // Assert
    Assert.Single(blocks);
    var block = blocks.First().Value;
    Assert.NotNull(block.Variables);
    Assert.Equal("http://localhost:5114", block.Variables["host"]);
    Assert.Equal("admin", block.Variables["username"]);
    Assert.False(block.IsTest);
  }

  [Fact]
  public void FindYamlBlocks_ShouldParseDependenciesInNonTestBlock()
  {
    // Arrange
    var markdown = """
# Config Block With Dependencies

```yaml
include:
  - variables.yaml
  - auth.resty
dependencies:
  - get_token
```
""";

    // Act
    var blocks = MarkdownParser.FindYamlBlocks(markdown, "test.md");

    // Assert
    Assert.Single(blocks);
    var block = blocks.First().Value;
    Assert.False(block.IsTest);
    Assert.NotNull(block.Include);
    Assert.NotNull(block.Dependencies);
    Assert.Contains("get_token", block.Dependencies);
  }

  [Fact]
  public void FindYamlBlocks_ShouldParseTestBlock()
  {
    // Arrange
    var markdown = """
# Authentication Test

```yaml
test: auth
post: $host/api/auth
body: |-
  {
    "Username": "$admin_username",
    "Password": "$env:ADMIN_PASSWORD"
  }
capture:
  token: auth.response.result.token
```
""";

    // Act
    var blocks = MarkdownParser.FindYamlBlocks(markdown, "test.md");

    // Assert
    Assert.Single(blocks);
    var block = blocks.First().Value;
    Assert.True(block.IsTest);
    Assert.Equal("auth", block.Test);
    Assert.Equal("$host/api/auth", block.Post);
    Assert.Contains("Username", block.Body?.ToString());
    Assert.NotNull(block.Capture);
    Assert.Equal("auth.response.result.token", block.Capture["token"]);
  }

  [Fact]
  public void FindYamlBlocks_ShouldHandleMultipleBlocks()
  {
    // Arrange
    var markdown = """
# Multiple Blocks Test

```yaml
variables:
  host: localhost
```

Some text between blocks.

```yaml
test: login
get: $host/login
```

```yaml
test: logout
post: $host/logout
```
""";

    // Act
    var blocks = MarkdownParser.FindYamlBlocks(markdown, "test.md");

    // Assert
    Assert.Equal(3, blocks.Count);

    // First block should be variables
    var firstBlock = blocks.OrderBy(kvp => kvp.Key).First().Value;
    Assert.NotNull(firstBlock.Variables);
    Assert.False(firstBlock.IsTest);

    // Second and third blocks should be tests
    var testBlocks = blocks.Values.Where(b => b.IsTest).ToList();
    Assert.Equal(2, testBlocks.Count);
    Assert.Contains(testBlocks, b => b.Test == "login");
    Assert.Contains(testBlocks, b => b.Test == "logout");
  }

  [Fact]
  public void FindYamlBlocks_ShouldThrowOnUnclosedBlock()
  {
    // Arrange
    var markdown = """
# Bad File

```yaml
variables:
  host: localhost
""";

    // Act & Assert
    var ex = Assert.Throws<InvalidOperationException>(() =>
        MarkdownParser.FindYamlBlocks(markdown, "bad.md"));

    Assert.Contains("Unclosed YAML code block", ex.Message);
    Assert.Contains("bad.md", ex.Message);
  }

  [Fact]
  public void ParseTestSuite_ShouldCreateCompleteTestSuite()
  {
    // Arrange
    var markdown = """
# Complete Test Suite

```yaml
include:
  - variables.yaml
variables:
  timeout: 30
```

## Auth Test

```yaml
test: authenticate
post: $host/api/auth
authorization: Bearer $token
```

## Verify Test

```yaml
test: verify
get: $host/api/verify
```
""";

    // Create a temporary file for testing
    var tempFile = Path.GetTempFileName();
    File.WriteAllText(tempFile, markdown);

    try {
      // Act
      var testSuite = MarkdownParser.ParseTestSuite(tempFile);

      // Assert
      Assert.Equal(tempFile, testSuite.FilePath);
      Assert.Single(testSuite.IncludeFiles);
      Assert.Equal("variables.yaml", testSuite.IncludeFiles.First());
      Assert.Single(testSuite.Variables);
      Assert.Equal(30, Convert.ToInt32(testSuite.Variables["timeout"]));
      Assert.Equal(2, testSuite.Tests.Count);
      Assert.Contains(testSuite.Tests, t => t.Name == "authenticate");
      Assert.Contains(testSuite.Tests, t => t.Name == "verify");
    } finally {
      File.Delete(tempFile);
    }
  }
}
