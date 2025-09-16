namespace Resty.Core.Variables;

using System.Text.RegularExpressions;

/// <summary>
/// Manages variable storage, precedence, and resolution for test execution.
/// Maintains state throughout the execution of a test suite.
/// </summary>
public class VariableStore
{
  private readonly Dictionary<string, object> _capturedVariables = new();
  private readonly Dictionary<string, object> _fileVariables = new();
  private readonly Dictionary<string, object> _includedVariables = new();

  // Regex patterns for variable substitution
  private static readonly Regex VariablePattern = new(@"\$([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.Compiled);
  private static readonly Regex EnvVariablePattern = new(@"\$env:([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.Compiled);

  /// <summary>
  /// Gets all variable names currently available.
  /// </summary>
  public IEnumerable<string> AllVariableNames {
    get {
      var names = new HashSet<string>();
      names.UnionWith(_includedVariables.Keys);
      names.UnionWith(_fileVariables.Keys);
      names.UnionWith(_capturedVariables.Keys);
      return names;
    }
  }

  /// <summary>
  /// Gets the current count of variables from all sources.
  /// </summary>
  public int TotalVariableCount => AllVariableNames.Count();

  /// <summary>
  /// Sets variables from included files (lowest precedence).
  /// </summary>
  /// <param name="variables">Variables from included YAML files.</param>
  public void SetIncludedVariables( Dictionary<string, object> variables )
  {
    _includedVariables.Clear();
    foreach (var (key, value) in variables) {
      _includedVariables[key] = value;
    }
  }

  /// <summary>
  /// Updates file-level variables (middle precedence).
  /// These accumulate as we process YAML blocks in the test file.
  /// </summary>
  /// <param name="variables">Variables from the current YAML block.</param>
  public void UpdateFileVariables( Dictionary<string, object> variables )
  {
    foreach (var (key, value) in variables) {
      _fileVariables[key] = value;
    }
  }

  /// <summary>
  /// Sets captured variables from test responses (highest precedence).
  /// </summary>
  /// <param name="variables">Variables captured from HTTP responses.</param>
  public void SetCapturedVariables( Dictionary<string, object> variables )
  {
    foreach (var (key, value) in variables) {
      _capturedVariables[key] = value;
    }
  }

  /// <summary>
  /// Gets a variable value following the precedence order:
  /// 1. Captured variables (highest)
  /// 2. File variables
  /// 3. Included variables
  /// 4. Environment variables (lowest)
  /// </summary>
  /// <param name="name">Variable name to look up.</param>
  /// <returns>Variable value or null if not found.</returns>
  public object? GetVariable( string name )
  {
    // Check captured variables first (highest precedence)
    if (_capturedVariables.TryGetValue(name, out var captured)) {
      return captured;
    }

    // Check file variables second
    if (_fileVariables.TryGetValue(name, out var fileVar)) {
      return fileVar;
    }

    // Check included variables third
    if (_includedVariables.TryGetValue(name, out var included)) {
      return included;
    }

    // Check environment variables last (lowest precedence)
    var envValue = Environment.GetEnvironmentVariable(name);
    if (envValue != null) {
      return envValue;
    }

    return null;
  }

  /// <summary>
  /// Resolves all variables in the given text using $variable and $env:VARIABLE syntax.
  /// </summary>
  /// <param name="text">Text containing variable references.</param>
  /// <returns>Text with all variables resolved.</returns>
  /// <exception cref="VariableNotFoundException">Thrown when a variable cannot be resolved.</exception>
  public string ResolveVariables( string? text )
  {
    if (string.IsNullOrEmpty(text)) {
      return string.Empty;
    }

    var result = text;

    // First, resolve environment variables ($env:VAR_NAME)
    result = EnvVariablePattern.Replace(result, match =>
    {
      var varName = match.Groups[1].Value;
      var envValue = Environment.GetEnvironmentVariable(varName);
      if (envValue == null) {
        throw new VariableNotFoundException($"Environment variable '{varName}' not found");
      }
      return envValue;
    });

    // Then resolve regular variables ($variable_name)
    result = VariablePattern.Replace(result, match =>
    {
      var varName = match.Groups[1].Value;

      // Skip if this looks like an environment variable (already processed)
      if (text.Contains($"$env:{varName}")) {
        return match.Value;
      }

      var value = GetVariable(varName);
      if (value == null) {
        throw new VariableNotFoundException($"Variable '{varName}' not found. Available variables: {string.Join(", ", AllVariableNames)}");
      }

      return value.ToString() ?? string.Empty;
    });

    return result;
  }

  /// <summary>
  /// Creates a snapshot of the current variable state for debugging.
  /// </summary>
  /// <returns>Dictionary containing all variables with their sources.</returns>
  public Dictionary<string, (object Value, string Source)> GetVariableSnapshot()
  {
    var snapshot = new Dictionary<string, (object Value, string Source)>();

    foreach (var (key, value) in _includedVariables) {
      snapshot[key] = (value, "Included");
    }

    foreach (var (key, value) in _fileVariables) {
      snapshot[key] = (value, "File");
    }

    foreach (var (key, value) in _capturedVariables) {
      snapshot[key] = (value, "Captured");
    }

    return snapshot;
  }

  /// <summary>
  /// Clears all variables (useful for testing).
  /// </summary>
  public void Clear()
  {
    _capturedVariables.Clear();
    _fileVariables.Clear();
    _includedVariables.Clear();
  }

  /// <summary>
  /// Creates a copy of the current variable store.
  /// </summary>
  /// <returns>A new VariableStore with the same variable state.</returns>
  public VariableStore Clone()
  {
    var clone = new VariableStore();

    foreach (var (key, value) in _includedVariables) {
      clone._includedVariables[key] = value;
    }

    foreach (var (key, value) in _fileVariables) {
      clone._fileVariables[key] = value;
    }

    foreach (var (key, value) in _capturedVariables) {
      clone._capturedVariables[key] = value;
    }

    return clone;
  }
}

/// <summary>
/// Exception thrown when a variable cannot be resolved.
/// </summary>
public class VariableNotFoundException : Exception
{
  public VariableNotFoundException( string message ) : base(message) { }
  public VariableNotFoundException( string message, Exception innerException ) : base(message, innerException) { }
}
