namespace Resty.Tests;

using Resty.Core.Execution;
using Resty.Core.Models;
using Resty.Core.Parsers;

public class TestSuiteRunnerTests
{
  [Fact]
  public async Task RunTestSuiteAsync_SkipsDependentAndTransitiveDependentAfterRequiredTestFails()
  {
    var results = await RunSuiteAsync("""
      # Dependency failure gating

      ```yaml
      test: required_failure
      mock_only: true
      mock:
        status: 500
        body:
          error: expected failure
      expect:
        status: 200
      ```

      ```yaml
      test: dependent
      requires: required_failure
      mock_only: true
      mock:
        status: 200
      expect:
        status: 200
      ```

      ```yaml
      test: transitive_dependent
      requires: dependent
      mock_only: true
      mock:
        status: 200
      expect:
        status: 200
      ```
      """);

    Assert.Equal(3, results.Count);
    Assert.Equal(TestStatus.Failed, results[0].Status);
    Assert.Equal(TestStatus.Skipped, results[1].Status);
    Assert.Equal(TestSkipReason.DependencyFailed, results[1].SkipReason);
    Assert.Contains("required_failure (failed)", results[1].ErrorMessage);
    Assert.Equal(TestStatus.Skipped, results[2].Status);
    Assert.Equal(TestSkipReason.DependencyFailed, results[2].SkipReason);
    Assert.Contains("dependent (dependency failed)", results[2].ErrorMessage);
  }

  [Fact]
  public async Task RunTestSuiteAsync_ConditionalFalseSkipSatisfiesDependent()
  {
    var results = await RunSuiteAsync("""
      # Existing resource workflow

      ```yaml
      test: sensor_lookup
      mock_only: true
      mock:
        status: 200
        body:
          found: true
      expect:
        status: [200, 404]
      capture:
        sensor_lookup_status: $response.status
      ```

      ```yaml
      test: sensor_create
      requires: sensor_lookup
      when: ${{ sensor_lookup_status == 404 }}
      mock_only: true
      mock:
        status: 201
      expect:
        status: 201
      ```

      ```yaml
      test: sensor_ready
      requires: sensor_create
      mock_only: true
      mock:
        status: 200
      expect:
        status: 200
      ```
      """);

    Assert.Equal(3, results.Count);
    Assert.Equal(TestStatus.Passed, results[0].Status);
    Assert.Equal(200, Assert.IsType<int>(results[0].ExtractedVariables["sensor_lookup_status"]));
    Assert.Equal(TestStatus.Skipped, results[1].Status);
    Assert.Equal(TestSkipReason.ConditionFalse, results[1].SkipReason);
    Assert.Contains("evaluated to false", results[1].ErrorMessage);
    Assert.Equal(TestStatus.Passed, results[2].Status);
  }

  [Fact]
  public async Task RunTestSuiteAsync_ConditionalTrueExecutesTest()
  {
    var results = await RunSuiteAsync("""
      # Missing resource workflow

      ```yaml
      test: sensor_lookup
      mock_only: true
      mock:
        status: 404
      expect:
        status: [200, 404]
      capture:
        sensor_lookup_status: $response.status
      ```

      ```yaml
      test: sensor_create
      requires: sensor_lookup
      when: ${{ sensor_lookup_status == 404 }}
      mock_only: true
      mock:
        status: 201
      expect:
        status: 201
      ```
      """);

    Assert.Equal(2, results.Count);
    Assert.All(results, result => Assert.Equal(TestStatus.Passed, result.Status));
    Assert.Equal(404, Assert.IsType<int>(results[0].ExtractedVariables["sensor_lookup_status"]));
  }

  [Fact]
  public async Task RunTestSuiteAsync_FailsTestWhenConditionIsNotBoolean()
  {
    var results = await RunSuiteAsync("""
      # Invalid condition

      ```yaml
      variables:
        invalid_condition: "not-a-boolean"
      ```

      ```yaml
      test: invalid_when
      when: $invalid_condition
      mock_only: true
      mock:
        status: 200
      expect:
        status: 200
      ```
      """);

    var result = Assert.Single(results);
    Assert.Equal(TestStatus.Failed, result.Status);
    Assert.Contains("must evaluate to a boolean", result.ErrorMessage);
    Assert.Null(result.RequestInfo);
  }

  [Fact]
  public async Task RunTestSuiteAsync_FailsTestWhenConditionVariableIsMissing()
  {
    var results = await RunSuiteAsync("""
      # Missing condition variable

      ```yaml
      test: missing_when_variable
      when: $missing_condition
      mock_only: true
      mock:
        status: 200
      expect:
        status: 200
      ```
      """);

    var result = Assert.Single(results);
    Assert.Equal(TestStatus.Failed, result.Status);
    Assert.Contains("Failed to evaluate when condition", result.ErrorMessage);
    Assert.Contains("Variable 'missing_condition' not found", result.ErrorMessage);
    Assert.Null(result.RequestInfo);
  }

  [Fact]
  public async Task RunTestSuiteAsync_DisabledTestDoesNotSatisfyDependent()
  {
    var results = await RunSuiteAsync("""
      # Disabled dependency

      ```yaml
      test: disabled_setup
      disabled: true
      mock_only: true
      mock:
        status: 200
      ```

      ```yaml
      test: dependent
      requires: disabled_setup
      mock_only: true
      mock:
        status: 200
      ```
      """);

    Assert.Equal(2, results.Count);
    Assert.Equal(TestSkipReason.Disabled, results[0].SkipReason);
    Assert.Equal(TestSkipReason.DependencyFailed, results[1].SkipReason);
    Assert.Contains("disabled_setup (disabled)", results[1].ErrorMessage);
  }

  [Fact]
  public async Task RunTestSuiteAsync_CommitsCaptureOnlyFromSuccessfulFinalAttempt()
  {
    var results = await RunSuiteAsync("""
      # Capture after expectation-aware retry

      ```yaml
      test: eventually_ready
      mock_only: true
      mock:
        sequence:
          - status: 200
            body:
              ready: false
              resourceId: first-attempt
          - status: 200
            body:
              ready: true
              resourceId: final-attempt
      expect:
        status: 200
        values:
          - key: $.ready
            op: equals
            value: true
      capture:
        resource_id: $.resourceId
      retry:
        attempts: 2
        delay: ${{ milliseconds(1) }}
        backoff: fixed
        expectation_failures: true
      ```

      ```yaml
      test: use_captured_resource
      requires: eventually_ready
      post: https://api.example.com/resources/use
      body:
        resourceId: $resource_id
      mock_only: true
      mock:
        status: 204
      expect:
        status: 204
      ```
      """);

    Assert.Equal(2, results.Count);
    Assert.All(results, result => Assert.True(result.Passed));
    Assert.Equal("final-attempt", results[0].ExtractedVariables["resource_id"]);
    Assert.Contains("\"resourceId\":\"final-attempt\"", results[1].RequestInfo?.Body);
    Assert.DoesNotContain("first-attempt", results[1].RequestInfo?.Body);
  }

  private static async Task<List<TestResult>> RunSuiteAsync( string content )
  {
    var tempDirectory = Path.Combine(
      Path.GetTempPath(),
      $"resty-workflow-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);
    var filePath = Path.Combine(tempDirectory, "workflow.resty");
    File.WriteAllText(filePath, content);

    try {
      var suite = MarkdownParser.ParseTestSuite(filePath);
      var executor = new HttpTestExecutor(new HttpClient());
      var runner = new TestSuiteRunner(executor);
      return await runner.RunTestSuiteAsync(suite);
    } finally {
      Directory.Delete(tempDirectory, recursive: true);
    }
  }
}
