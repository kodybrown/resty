namespace Resty.Core.Parsers;

using Resty.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

/// <summary>
/// Simple, efficient parser for extracting YAML code blocks from Markdown files.
/// </summary>
public static class MarkdownParser
{
  private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
      .WithNamingConvention(CamelCaseNamingConvention.Instance)
      .WithTypeConverter(new RequiresConverter())
      .IgnoreUnmatchedProperties()
      .Build();

  /// <summary>
  /// Finds all YAML code blocks in a Markdown file.
  /// </summary>
  /// <param name="filePath">Path to the Markdown file.</param>
  /// <returns>Dictionary where key is line number and value is the parsed YamlBlock.</returns>
  /// <exception cref="FileNotFoundException">Thrown if the file doesn't exist.</exception>
  /// <exception cref="InvalidOperationException">Thrown if YAML parsing fails.</exception>
  public static Dictionary<int, YamlBlock> FindYamlBlocks( string filePath )
  {
    if (!File.Exists(filePath)) {
      throw new FileNotFoundException($"Markdown file not found: {filePath}");
    }

    var content = File.ReadAllText(filePath);
    return FindYamlBlocks(content, filePath);
  }

  /// <summary>
  /// Finds all YAML code blocks in Markdown content.
  /// </summary>
  /// <param name="content">Markdown content to parse.</param>
  /// <param name="filePath">File path for error reporting.</param>
  /// <returns>Dictionary where key is line number and value is the parsed YamlBlock.</returns>
  public static Dictionary<int, YamlBlock> FindYamlBlocks( string content, string filePath = "" )
  {
    var blocks = new Dictionary<int, YamlBlock>();
    var lines = content.Split('\n');

    for (var i = 0; i < lines.Length; i++) {
      var line = lines[i].TrimEnd(['\r', '\n']); // Handle Windows line endings

      // Look for start of YAML code block
      if (line.Trim() == "```yaml") {
        var startLine = i + 1; // 1-based line numbering
        var yamlContent = new List<string>();
        var endFound = false;

        // Collect YAML content until we find the closing ```
        for (var j = i + 1; j < lines.Length; j++) {
          var yamlLine = lines[j].TrimEnd('\r');

          if (yamlLine.Trim() == "```") {
            endFound = true;
            i = j; // Move outer loop past this block
            break;
          }

          yamlContent.Add(yamlLine);
        }

        if (!endFound) {
          throw new InvalidOperationException(
              $"Unclosed YAML code block starting at line {startLine} in file: {filePath}");
        }

        // Parse the YAML content
        if (yamlContent.Count > 0) {
          var yamlText = string.Join('\n', yamlContent);
          var yamlBlock = ParseYamlContent(yamlText, filePath, startLine);

          if (yamlBlock != null) {
            blocks[startLine] = yamlBlock;
          }
        }
      }
    }

    return blocks;
  }

  /// <summary>
  /// Parses YAML content into a YamlBlock.
  /// </summary>
  /// <param name="yamlContent">Raw YAML content.</param>
  /// <param name="filePath">Source file path for error reporting.</param>
  /// <param name="lineNumber">Line number where the YAML block starts.</param>
  /// <returns>Parsed YamlBlock or null if the YAML is empty/invalid.</returns>
  private static YamlBlock? ParseYamlContent( string yamlContent, string filePath, int lineNumber )
  {
    if (string.IsNullOrWhiteSpace(yamlContent)) {
      return null;
    }

    try {
      // YamlDotNet can deserialize directly to our record type
      var yamlBlock = YamlDeserializer.Deserialize<YamlBlock>(yamlContent);
      return yamlBlock;
    } catch (Exception ex) {
      throw new InvalidOperationException(
        $"Failed to parse YAML block at line {lineNumber} in file '{filePath}': {ex.GetType().Name}: {ex.Message}", ex);
    }
  }

  /// <summary>
  /// Parses a Markdown file and returns a TestSuite with all tests and variables.
  /// </summary>
  /// <param name="filePath">Path to the Markdown file.</param>
  /// <returns>Parsed TestSuite containing tests and variables.</returns>
  public static TestSuite ParseTestSuite( string filePath )
  {
    var yamlBlocks = FindYamlBlocks(filePath);
    var tests = new List<HttpTest>();
    var variables = new Dictionary<string, object>();
    var includeFiles = new List<string>();

    foreach (var (lineNumber, block) in yamlBlocks.OrderBy(kvp => kvp.Key)) {
      // Collect variables from this block
      if (block.Variables != null) {
        foreach (var (key, value) in block.Variables) {
          variables[key] = value;
        }
      }

      // Collect include files
      if (block.Include != null) {
        includeFiles.AddRange(block.Include);
      }

      // Create HttpTest if this is a valid test block
      if (block.IsTest) {
        try {
          var httpTest = HttpTest.FromYamlBlock(block, filePath, lineNumber);
          tests.Add(httpTest);
        } catch (Exception ex) {
          throw new InvalidOperationException(
            $"Failed to create test '{block.Test}' at line {lineNumber} in file '{filePath}': {ex.Message}", ex);
        }
      }
    }

    return new TestSuite {
      FilePath = filePath,
      Variables = variables,
      IncludeFiles = includeFiles,
      Tests = tests
    };
  }
}
