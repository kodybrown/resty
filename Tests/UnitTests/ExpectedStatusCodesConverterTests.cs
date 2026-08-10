namespace Resty.Tests;

using Resty.Core.Parsers;

public class ExpectedStatusCodesConverterTests
{
  [Fact]
  public void ParseYaml_WithScalarExpectedStatus_PreservesCompatibility()
  {
    var block = ParseSingleBlock("""
      ```yaml
      test: scalar_status
      get: https://example.test
      expect:
        status: 204
      ```
      """);

    Assert.Equal(new[] { 204 }, block.Expect?.Status?.Codes);
  }

  [Fact]
  public void ParseYaml_WithExpectedStatusList_ParsesAllCodes()
  {
    var block = ParseSingleBlock("""
      ```yaml
      test: status_list
      get: https://example.test
      expect:
        status: [200, 404]
      ```
      """);

    Assert.Equal(new[] { 200, 404 }, block.Expect?.Status?.Codes);
  }

  [Fact]
  public void ParseYaml_WithNonIntegerExpectedStatus_FailsClearly()
  {
    var exception = Assert.Throws<InvalidOperationException>(() => ParseSingleBlock("""
      ```yaml
      test: invalid_status
      get: https://example.test
      expect:
        status: [200, nope]
      ```
      """));

    Assert.Contains("expect.status value 'nope' is not an integer", exception.Message);
  }

  private static Resty.Core.Models.YamlBlock ParseSingleBlock( string content )
  {
    return Assert.Single(MarkdownParser.FindYamlBlocks(content, "status.resty")).Value;
  }
}
