namespace Resty.Core.Output;

using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Resty.Core.Models;

/// <summary>
/// Formats test results as JUnit XML using strongly typed models and XmlSerializer.
/// </summary>
public class XmlOutputFormatter : IOutputFormatter
{
  public void FormatAndWrite( TestRunSummary summary, bool verbose = false )
  {
    var xml = ConvertToXmlString(summary);
    Console.WriteLine(xml);
  }

  public async Task SaveAsync( TestRunSummary summary, string filePath )
  {
    var xml = ConvertToXmlString(summary);
    await File.WriteAllTextAsync(filePath, xml, Encoding.UTF8);
  }

  private string ConvertToXmlString( TestRunSummary summary )
  {
    var junitModel = ConvertToJUnitModel(summary);

    var serializer = new XmlSerializer(typeof(JUnitTestSuites));
    var settings = new XmlWriterSettings {
      Indent = true,
      IndentChars = "  ",
      Encoding = Encoding.UTF8,
      OmitXmlDeclaration = false
    };

    using var stringWriter = new StringWriter();
    using var xmlWriter = XmlWriter.Create(stringWriter, settings);

    serializer.Serialize(xmlWriter, junitModel);
    return stringWriter.ToString();
  }

  private JUnitTestSuites ConvertToJUnitModel( TestRunSummary summary )
  {
    var testSuites = new JUnitTestSuites {
      Tests = summary.TotalTests,
      Failures = summary.FailedTests,
      Time = summary.TotalDuration.TotalSeconds.ToString("F3"),
      Timestamp = summary.StartTime
    };

    // Group tests by source file to create test suites
    var resultsByFile = summary.Results.GroupBy(r => r.Test.SourceFile).ToList();

    foreach (var fileGroup in resultsByFile) {
      var testSuite = new JUnitTestSuite {
        Name = Path.GetFileNameWithoutExtension(fileGroup.Key),
        Tests = fileGroup.Count(),
        Failures = fileGroup.Count(r => r.Status == TestStatus.Failed),
        Time = fileGroup.Sum(r => r.Duration.TotalSeconds).ToString("F3"),
        Timestamp = fileGroup.Min(r => summary.StartTime) // Use the summary start time as baseline
      };

      foreach (var result in fileGroup.OrderBy(r => r.Test.Name)) {
        var testCase = new JUnitTestCase {
          Name = result.Test.Name,
          ClassName = $"{Path.GetFileNameWithoutExtension(fileGroup.Key)}.{SanitizeClassName(result.Test.Method)}Tests",
          Time = result.Duration.TotalSeconds.ToString("F3")
        };

        // Add failure information if the test failed
        if (result.Status == TestStatus.Failed) {
          testCase.Failure = new JUnitFailure {
            Message = result.ErrorMessage ?? "Test failed",
            Type = "AssertionError",
            Details = BuildFailureDetails(result)
          };
        }

        // Add system output for additional context
        var systemOut = BuildSystemOutput(result);
        if (!string.IsNullOrEmpty(systemOut)) {
          testCase.SystemOut = systemOut;
        }

        testSuite.TestCases.Add(testCase);
      }

      testSuites.TestSuites.Add(testSuite);
    }

    return testSuites;
  }

  private string BuildFailureDetails( TestResult result )
  {
    var details = new StringBuilder();

    details.AppendLine($"HTTP {result.Test.Method} {result.RequestInfo?.Url ?? result.Test.Url}");

    if (result.StatusCode.HasValue) {
      details.AppendLine($"Status Code: {result.StatusCode}");
    }

    if (!string.IsNullOrEmpty(result.ErrorMessage)) {
      details.AppendLine($"Error: {result.ErrorMessage}");
    }

    if (!string.IsNullOrEmpty(result.ResponseBody)) {
      details.AppendLine("Response Body:");
      details.AppendLine(result.ResponseBody);
    }

    return details.ToString().Trim();
  }

  private string BuildSystemOutput( TestResult result )
  {
    var output = new StringBuilder();

    output.AppendLine($"Test: {result.Test.Name}");
    output.AppendLine($"HTTP {result.Test.Method} {result.RequestInfo?.Url ?? result.Test.Url}");
    output.AppendLine($"Duration: {result.Duration.TotalSeconds:F3}s");

    if (result.StatusCode.HasValue) {
      output.AppendLine($"Status Code: {result.StatusCode}");
    }

    if (result.ExtractedVariables?.Count > 0) {
      output.AppendLine("Extracted Variables:");
      foreach (var variable in result.ExtractedVariables) {
        output.AppendLine($"  {variable.Key}: {variable.Value}");
      }
    }

    if (result.RequestInfo?.Headers?.Count > 0) {
      output.AppendLine("Request Headers:");
      foreach (var header in result.RequestInfo.Headers) {
        output.AppendLine($"  {header.Key}: {header.Value}");
      }
    }

    if (!string.IsNullOrEmpty(result.RequestInfo?.Body)) {
      output.AppendLine("Request Body:");
      output.AppendLine(result.RequestInfo.Body);
    }

    return output.ToString().Trim();
  }

  private static string SanitizeClassName( string input )
  {
    // Convert HTTP method to a valid class name component
    return input?.ToUpper() switch {
      "GET" => "Get",
      "POST" => "Post",
      "PUT" => "Put",
      "DELETE" => "Delete",
      "PATCH" => "Patch",
      "HEAD" => "Head",
      "OPTIONS" => "Options",
      _ => "Http"
    };
  }
}
