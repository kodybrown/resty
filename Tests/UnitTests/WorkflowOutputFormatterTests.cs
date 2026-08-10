namespace Resty.Tests;

using Resty.Core.Models;
using Resty.Core.Output;

public class WorkflowOutputFormatterTests
{
  [Fact]
  public async Task Formatters_RetainConditionalSkipClassificationAndReason()
  {
    var test = new HttpTest {
      Name = "conditional_test",
      Method = "GET",
      Url = "https://example.test",
      SourceFile = "workflow.resty",
      SourceLine = 12
    };
    var result = TestResult.Skipped(
      test,
      "when condition evaluated to false",
      skipReason: TestSkipReason.ConditionFalse);
    var passedResult = new TestResult {
      Test = test with { Name = "passed_test" },
      Status = TestStatus.Passed,
      StartTime = result.StartTime,
      EndTime = result.EndTime
    };
    var summary = TestRunSummary.Create(
      result.StartTime,
      result.EndTime,
      new[] { passedResult, result },
      Array.Empty<TestSuite>());

    var tempDirectory = Path.Combine(Path.GetTempPath(), $"resty-output-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDirectory);

    try {
      var outputs = new (string Name, IOutputFormatter Formatter)[] {
        ("console.txt", new ConsoleOutputFormatter()),
        ("results.md", new MarkdownOutputFormatter()),
        ("results.json", new JsonOutputFormatter()),
        ("agent.json", new AgentOutputFormatter()),
        ("results.xml", new XmlOutputFormatter())
      };

      foreach (var (name, formatter) in outputs) {
        var outputPath = Path.Combine(tempDirectory, name);
        await formatter.SaveToFileAsync(summary, outputPath);
        var output = File.ReadAllText(outputPath);
        Assert.Contains("when condition evaluated to false", output);
      }

      var jsonOutput = File.ReadAllText(Path.Combine(tempDirectory, "results.json"));
      Assert.Contains("\"skipReason\": \"ConditionFalse\"", jsonOutput);

      var agentOutput = File.ReadAllText(Path.Combine(tempDirectory, "agent.json"));
      Assert.Contains("\"skipReason\":\"conditionfalse\"", agentOutput);

      var xmlOutput = File.ReadAllText(Path.Combine(tempDirectory, "results.xml"));
      Assert.Contains("<skipped message=\"when condition evaluated to false\"", xmlOutput);
    } finally {
      Directory.Delete(tempDirectory, recursive: true);
    }
  }
}
