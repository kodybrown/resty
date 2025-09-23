namespace Resty.Core.Output;

using System.Text.Json.Serialization;
using System.Xml.Serialization;

/// <summary>
/// JSON output model for test results.
/// </summary>
public class JsonTestOutput
{
  [JsonPropertyName("summary")]
  public JsonSummary Summary { get; set; } = new();

  [JsonPropertyName("results")]
  public List<JsonTestResult> Results { get; set; } = new();

  [JsonPropertyName("metadata")]
  public JsonMetadata Metadata { get; set; } = new();
}

public class JsonSummary
{
  [JsonPropertyName("totalTests")]
  public int TotalTests { get; set; }

  [JsonPropertyName("passedTests")]
  public int PassedTests { get; set; }

  [JsonPropertyName("failedTests")]
  public int FailedTests { get; set; }

  [JsonPropertyName("skippedTests")]
  public int SkippedTests { get; set; }

  [JsonPropertyName("passRate")]
  public double PassRate { get; set; }

  [JsonPropertyName("duration")]
  public double Duration { get; set; }

  [JsonPropertyName("startTime")]
  public DateTime StartTime { get; set; }

  [JsonPropertyName("endTime")]
  public DateTime EndTime { get; set; }
}

public class JsonTestResult
{
  [JsonPropertyName("test")]
  public string Test { get; set; } = string.Empty;

  [JsonPropertyName("description")]
  public string? Description { get; set; }

  [JsonPropertyName("file")]
  public string File { get; set; } = string.Empty;

  [JsonPropertyName("method")]
  public string Method { get; set; } = string.Empty;

  [JsonPropertyName("url")]
  public string Url { get; set; } = string.Empty;

  [JsonPropertyName("status")]
  public string Status { get; set; } = string.Empty;

  [JsonPropertyName("duration")]
  public double Duration { get; set; }

  [JsonPropertyName("statusCode")]
  public int? StatusCode { get; set; }

  [JsonPropertyName("errorMessage")]
  public string? ErrorMessage { get; set; }

  [JsonPropertyName("extractedVariables")]
  public Dictionary<string, object> ExtractedVariables { get; set; } = new();

  [JsonPropertyName("request")]
  public JsonRequestInfo? Request { get; set; }

  [JsonPropertyName("response")]
  public JsonResponseInfo? Response { get; set; }

  [JsonPropertyName("variableSnapshot")]
  public Dictionary<string, JsonVariableInfo> VariableSnapshot { get; set; } = new();
}

public class JsonRequestInfo
{
  [JsonPropertyName("headers")]
  public Dictionary<string, string> Headers { get; set; } = new();

  [JsonPropertyName("body")]
  public string? Body { get; set; }
}

public class JsonResponseInfo
{
  [JsonPropertyName("headers")]
  public Dictionary<string, string> Headers { get; set; } = new();

  [JsonPropertyName("body")]
  public string? Body { get; set; }
}

public class JsonVariableInfo
{
  [JsonPropertyName("value")]
  public object Value { get; set; } = null!;

  [JsonPropertyName("source")]
  public string Source { get; set; } = string.Empty;
}

public class JsonMetadata
{
  [JsonPropertyName("tool")]
  public string Tool { get; set; } = "Resty";

  [JsonPropertyName("version")]
  public string Version { get; set; } = "1.0.0";

  [JsonPropertyName("timestamp")]
  public DateTime Timestamp { get; set; } = DateTime.UtcNow;

  [JsonPropertyName("environment")]
  public JsonEnvironment Environment { get; set; } = new();
}

public class JsonEnvironment
{
  [JsonPropertyName("os")]
  public string OS { get; set; } = Environment.OSVersion.ToString();

  [JsonPropertyName("runtime")]
  public string Runtime { get; set; } = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

  [JsonPropertyName("machine")]
  public string Machine { get; set; } = Environment.MachineName;
}

/// <summary>
/// XML output models for JUnit format.
/// </summary>
[XmlRoot("testsuites")]
public class JUnitTestSuites
{
  [XmlAttribute("tests")]
  public int Tests { get; set; }

  [XmlAttribute("failures")]
  public int Failures { get; set; }

  [XmlAttribute("time")]
  public string Time { get; set; } = "0";

  [XmlAttribute("timestamp")]
  public DateTime Timestamp { get; set; } = DateTime.UtcNow;

  [XmlElement("testsuite")]
  public List<JUnitTestSuite> TestSuites { get; set; } = new();
}

public class JUnitTestSuite
{
  [XmlAttribute("name")]
  public string Name { get; set; } = string.Empty;

  [XmlAttribute("tests")]
  public int Tests { get; set; }

  [XmlAttribute("failures")]
  public int Failures { get; set; }

  [XmlAttribute("time")]
  public string Time { get; set; } = "0";

  [XmlAttribute("timestamp")]
  public DateTime Timestamp { get; set; } = DateTime.UtcNow;

  [XmlElement("testcase")]
  public List<JUnitTestCase> TestCases { get; set; } = new();
}

public class JUnitTestCase
{
  [XmlAttribute("classname")]
  public string ClassName { get; set; } = string.Empty;

  [XmlAttribute("name")]
  public string Name { get; set; } = string.Empty;

  [XmlAttribute("time")]
  public string Time { get; set; } = "0";

  [XmlElement("failure")]
  public JUnitFailure? Failure { get; set; }

  [XmlElement("system-out")]
  public string? SystemOut { get; set; }

  [XmlElement("system-err")]
  public string? SystemErr { get; set; }
}

public class JUnitFailure
{
  [XmlAttribute("message")]
  public string Message { get; set; } = string.Empty;

  [XmlAttribute("type")]
  public string Type { get; set; } = "AssertionError";

  [XmlText]
  public string Details { get; set; } = string.Empty;
}
