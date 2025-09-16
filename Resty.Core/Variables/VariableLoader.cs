namespace Resty.Core.Variables;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

/// <summary>
/// Loads variables from YAML files with include support and cycle detection.
/// </summary>
public static class VariableLoader
{
  private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
    .WithNamingConvention(NullNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

  /// <summary>
  /// Loads variables from a list of include files, resolving any nested includes.
  /// Maintains the order of inclusion for proper variable precedence.
  /// </summary>
  /// <param name="includeFiles">List of YAML files to include.</param>
  /// <param name="baseDirectory">Base directory for resolving relative paths.</param>
  /// <returns>Dictionary of all variables from included files.</returns>
  /// <exception cref="FileNotFoundException">Thrown when an include file is not found.</exception>
  /// <exception cref="InvalidOperationException">Thrown when a circular include is detected.</exception>
  public static Dictionary<string, object> LoadIncludedVariables(
      List<string> includeFiles,
      string baseDirectory )
  {
    var allVariables = new Dictionary<string, object>();
    var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var processingStack = new Stack<string>();

    foreach (var includeFile in includeFiles) {
      LoadVariablesRecursive(includeFile, baseDirectory, allVariables, processedFiles, processingStack);
    }

    return allVariables;
  }

  /// <summary>
  /// Recursively loads variables from a YAML file and any files it includes.
  /// </summary>
  private static void LoadVariablesRecursive(
      string includeFile,
      string baseDirectory,
      Dictionary<string, object> allVariables,
      HashSet<string> processedFiles,
      Stack<string> processingStack )
  {
    // Resolve the full path
    var fullPath = Path.IsPathRooted(includeFile)
      ? includeFile
      : Path.GetFullPath(Path.Combine(baseDirectory, includeFile));

    // Check for circular includes
    if (processingStack.Contains(fullPath, StringComparer.OrdinalIgnoreCase)) {
      var cycle = string.Join(" -> ", processingStack.Reverse().Concat(new[] { fullPath }));
      throw new InvalidOperationException($"Circular include detected: {cycle}");
    }

    // Skip if already processed
    if (processedFiles.Contains(fullPath)) {
      return;
    }

    // Check if file exists
    if (!File.Exists(fullPath)) {
      throw new FileNotFoundException($"Include file not found: {fullPath}");
    }

    // Add to processing stack
    processingStack.Push(fullPath);
    processedFiles.Add(fullPath);

    try {
      // Load and parse the YAML file
      var yamlContent = File.ReadAllText(fullPath);
      var yamlData = ParseYamlFile(yamlContent, fullPath);

      // Process any nested includes first (so they have lower precedence)
      if (yamlData.Include != null) {
        var fileDirectory = Path.GetDirectoryName(fullPath) ?? baseDirectory;
        foreach (var nestedInclude in yamlData.Include) {
          LoadVariablesRecursive(nestedInclude, fileDirectory, allVariables, processedFiles, processingStack);
        }
      }

      // Add variables from this file (higher precedence than nested includes)
      if (yamlData.Variables != null) {
        foreach (var (key, value) in yamlData.Variables) {
          allVariables[key] = value;
        }
      }
    } finally {
      // Remove from processing stack
      processingStack.Pop();
    }
  }

  /// <summary>
  /// Parses a YAML file content and extracts variables and includes.
  /// </summary>
  private static YamlFileData ParseYamlFile( string yamlContent, string filePath )
  {
    try {
      // Always parse as raw dictionary first for maximum compatibility
      var rawData = YamlDeserializer.Deserialize<Dictionary<string, object>>(yamlContent);
      if (rawData == null) {
        return new YamlFileData();
      }

      var result = new YamlFileData();

      // Handle include directive
      if (rawData.TryGetValue("include", out var includeObj)) {
        if (includeObj is string singleInclude) {
          result.Include = new List<string> { singleInclude };
        } else if (includeObj is List<object> includeList) {
          result.Include = includeList.Select(i => i.ToString() ?? string.Empty).ToList();
        }
      }

      // Handle variables
      if (rawData.TryGetValue("variables", out var variablesObj)) {
        // File has explicit variables section
        if (variablesObj is Dictionary<object, object> varsDict) {
          result.Variables = varsDict.ToDictionary(
            kvp => kvp.Key.ToString() ?? string.Empty,
            kvp => kvp.Value);
        } else if (variablesObj is Dictionary<string, object> stringVarsDict) {
          result.Variables = stringVarsDict;
        }
      } else {
        // No explicit variables section - treat entire file as variables
        // But exclude 'include' key if it exists
        result.Variables = rawData
              .Where(kvp => kvp.Key != "include")
              .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
      }

      return result;
    } catch (Exception ex) {
      throw new InvalidOperationException(
          $"Failed to parse YAML file '{filePath}': {ex.Message}", ex);
    }
  }

  /// <summary>
  /// Data structure for parsing YAML files that may contain variables and includes.
  /// </summary>
  private class YamlFileData
  {
    public List<string>? Include { get; set; }
    public Dictionary<string, object>? Variables { get; set; }
  }
}
