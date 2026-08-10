namespace Resty.Tests;

using Resty.Core.Models;
using Resty.Core.Parsers;

public class RetryPolicyDefinitionConverterTests
{
  [Fact]
  public void ParseYaml_WithScalarRetry_PreservesLegacyRetryCountSemantics()
  {
    var block = ParseSingleBlock("""
      ```yaml
      test: legacy_retry
      get: https://example.test
      retry: 2
      ```
      """);

    Assert.NotNull(block.Retry);
    Assert.Equal(3, block.Retry.Attempts);
    Assert.Equal(TimeSpan.FromSeconds(1), block.Retry.Delay);
    Assert.Equal(RetryBackoffKind.Exponential, block.Retry.Backoff);
    Assert.Equal(new[] { 408, 429, 500, 502, 503, 504 }, block.Retry.Statuses.Codes);
    Assert.False(block.Retry.ExpectationFailures);
  }

  [Fact]
  public void ParseYaml_WithStructuredRetry_ParsesPolicyAndWaitBefore()
  {
    var block = ParseSingleBlock("""
      ```yaml
      test: structured_retry
      get: https://example.test
      wait_before: ${{ seconds(2) }}
      retry:
        attempts: 10
        delay: ${{ milliseconds(500) }}
        backoff: fixed
        statuses: [404, 409, 500]
        expectation_failures: true
      ```
      """);

    Assert.Equal("${{ seconds(2) }}", block.WaitBefore);
    Assert.NotNull(block.Retry);
    Assert.Equal(10, block.Retry.Attempts);
    Assert.Equal("${{ milliseconds(500) }}", block.Retry.Delay);
    Assert.Equal(RetryBackoffKind.Fixed, block.Retry.Backoff);
    Assert.Equal(new[] { 404, 409, 500 }, block.Retry.Statuses.Codes);
    Assert.True(block.Retry.ExpectationFailures);
  }

  [Fact]
  public void ParseYaml_WithUnsupportedBackoff_FailsClearly()
  {
    var exception = Assert.Throws<InvalidOperationException>(() => ParseSingleBlock("""
      ```yaml
      test: invalid_backoff
      get: https://example.test
      retry:
        attempts: 2
        backoff: exponential
      ```
      """));

    Assert.Contains("retry.backoff currently supports only 'fixed'", exception.Message);
  }

  [Fact]
  public void ParseYaml_WithZeroStructuredAttempts_FailsClearly()
  {
    var exception = Assert.Throws<InvalidOperationException>(() => ParseSingleBlock("""
      ```yaml
      test: invalid_attempts
      get: https://example.test
      retry:
        attempts: 0
      ```
      """));

    Assert.Contains("retry.attempts must be at least 1", exception.Message);
  }

  private static YamlBlock ParseSingleBlock( string content )
  {
    return Assert.Single(MarkdownParser.FindYamlBlocks(content, "retry.resty")).Value;
  }
}
