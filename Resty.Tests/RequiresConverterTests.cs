namespace Resty.Tests;

using Resty.Core.Models;
using Resty.Core.Parsers;

public class RequiresConverterTests
{
  [Fact]
  public void ParseYaml_WithSingleStringRequires_ShouldParseCorrectly()
  {
    // Arrange
    var yamlContent = @"
```yaml
test: testA
requires: testB
get: /api/testA
```
";

    // Act
    var blocks = MarkdownParser.FindYamlBlocks(yamlContent, "test.yaml");

    // Assert
    Assert.Single(blocks);
    var block = blocks.Values.First();
    Assert.Equal("testA", block.Test);
    Assert.NotNull(block.Requires);
    Assert.Single(block.Requires);
    Assert.Equal("testB", block.Requires[0]);
  }

  [Fact]
  public void ParseYaml_WithArrayRequires_ShouldParseCorrectly()
  {
    // Arrange
    var yamlContent = @"
```yaml
test: testA
requires:
  - testB
  - testC
get: /api/testA
```
";

    // Act
    var blocks = MarkdownParser.FindYamlBlocks(yamlContent, "test.yaml");

    // Assert
    Assert.Single(blocks);
    var block = blocks.Values.First();
    Assert.Equal("testA", block.Test);
    Assert.NotNull(block.Requires);
    Assert.Equal(2, block.Requires.Count);
    Assert.Contains("testB", block.Requires);
    Assert.Contains("testC", block.Requires);
  }

  [Fact]
  public void ParseYaml_WithNoRequires_ShouldHaveNullRequires()
  {
    // Arrange
    var yamlContent = @"
```yaml
test: testA
get: /api/testA
```
";

    // Act
    var blocks = MarkdownParser.FindYamlBlocks(yamlContent, "test.yaml");

    // Assert
    Assert.Single(blocks);
    var block = blocks.Values.First();
    Assert.Equal("testA", block.Test);
    Assert.Null(block.Requires);
  }

  [Fact]
  public void ParseYaml_WithEmptyArrayRequires_ShouldHaveNullRequires()
  {
    // Arrange
    var yamlContent = @"
```yaml
test: testA
requires: []
get: /api/testA
```
";

    // Act
    var blocks = MarkdownParser.FindYamlBlocks(yamlContent, "test.yaml");

    // Assert
    Assert.Single(blocks);
    var block = blocks.Values.First();
    Assert.Equal("testA", block.Test);
    Assert.Null(block.Requires); // Empty array should result in null
  }

  [Fact]
  public void ParseMarkdown_WithRequiresInCodeBlock_ShouldParseCorrectly()
  {
    // Arrange
    var markdownContent = @"
# Test with Dependencies

```yaml
test: login
post: /api/auth
body: |
  {
    ""username"": ""user"",
    ""password"": ""pass""
  }
capture:
  token: $.token
```

```yaml
test: getProfile
requires: login
get: /api/profile
authorization: Bearer $token
```

```yaml
test: updateProfile
requires:
  - login
  - getProfile
put: /api/profile
authorization: Bearer $token
body: |
  {
    ""name"": ""Updated Name""
  }
```
";

    // Act
    var blocks = MarkdownParser.FindYamlBlocks(markdownContent, "test.md");

    // Assert
    Assert.Equal(3, blocks.Count);

    var loginBlock = blocks.Values.First(b => b.Test == "login");
    Assert.Null(loginBlock.Requires);

    var profileBlock = blocks.Values.First(b => b.Test == "getProfile");
    Assert.NotNull(profileBlock.Requires);
    Assert.Single(profileBlock.Requires);
    Assert.Equal("login", profileBlock.Requires[0]);

    var updateBlock = blocks.Values.First(b => b.Test == "updateProfile");
    Assert.NotNull(updateBlock.Requires);
    Assert.Equal(2, updateBlock.Requires.Count);
    Assert.Contains("login", updateBlock.Requires);
    Assert.Contains("getProfile", updateBlock.Requires);
  }
}
