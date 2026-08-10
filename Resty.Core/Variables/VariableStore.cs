namespace Resty.Core.Variables;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Manages typed variable storage, precedence, expression evaluation, and substitution.
/// Maintains state throughout the execution of a test suite.
/// </summary>
public class VariableStore
{
  private readonly Dictionary<string, object> _capturedVariables = new();
  private readonly Dictionary<string, object> _fileVariables = new();
  private readonly Dictionary<string, object> _includedVariables = new();
  private readonly Dictionary<string, object> _resolvedVariables = new();
  private readonly Dictionary<string, HashSet<string>> _variableDependencies = new();
  private readonly DateTimeOffset _utcNow;
  private readonly ExpressionEvaluator _expressionEvaluator;

  private static readonly Regex VariablePattern = new(
    @"\$([a-zA-Z_][a-zA-Z0-9_]*)",
    RegexOptions.Compiled);
  private static readonly Regex ExactVariablePattern = new(
    @"^\$([a-zA-Z_][a-zA-Z0-9_]*)$",
    RegexOptions.Compiled);
  private static readonly Regex EnvVariablePattern = new(
    @"\$env:([a-zA-Z_][a-zA-Z0-9_]*)",
    RegexOptions.Compiled);
  private static readonly Regex ExactEnvVariablePattern = new(
    @"^\$env:([a-zA-Z_][a-zA-Z0-9_]*)$",
    RegexOptions.Compiled);
  private static readonly Regex ExpressionPattern = new(
    @"\$\{\{\s*(.*?)\s*\}\}",
    RegexOptions.Compiled | RegexOptions.Singleline);
  private static readonly Regex ExactExpressionPattern = new(
    @"^\$\{\{\s*(.*?)\s*\}\}$",
    RegexOptions.Compiled | RegexOptions.Singleline);

  // Preserve the existing $RandomNumber behavior for backward compatibility.
  private static readonly Random _randomGenerator = new();

  public VariableStore( TimeProvider? timeProvider = null )
    : this((timeProvider ?? TimeProvider.System).GetUtcNow()) { }

  private VariableStore( DateTimeOffset utcNow )
  {
    _utcNow = utcNow.ToUniversalTime();
    _expressionEvaluator = new ExpressionEvaluator(_utcNow);
  }

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
  public void SetIncludedVariables( Dictionary<string, object> variables )
  {
    _includedVariables.Clear();
    foreach (var (key, value) in variables) {
      _includedVariables[key] = value;
    }
    _resolvedVariables.Clear();
    _variableDependencies.Clear();
  }

  /// <summary>
  /// Updates file-level variables (middle precedence).
  /// </summary>
  public void UpdateFileVariables( Dictionary<string, object> variables )
  {
    foreach (var (key, value) in variables) {
      _fileVariables[key] = value;
    }
    InvalidateVariables(variables.Keys);
  }

  /// <summary>
  /// Sets captured variables from test responses (highest precedence).
  /// </summary>
  public void SetCapturedVariables( Dictionary<string, object> variables )
  {
    foreach (var (key, value) in variables) {
      _capturedVariables[key] = value;
    }
    InvalidateVariables(variables.Keys);
  }

  /// <summary>
  /// Gets a typed variable value following Resty's existing precedence order.
  /// Computed variables are evaluated lazily, cached, and cycle checked.
  /// </summary>
  public object? GetVariable( string name )
  {
    return ResolveVariable(name, new Stack<string>());
  }

  /// <summary>
  /// Evaluates a bounded Resty expression and returns its typed result.
  /// </summary>
  public object EvaluateExpression( string expression )
  {
    var resolutionStack = new Stack<string>();
    return _expressionEvaluator.Evaluate(
      expression,
      name => ResolveVariable(name, resolutionStack));
  }

  /// <summary>
  /// Resolves a scalar value. Exact variable and expression scalars preserve their type;
  /// larger strings are interpolated using invariant formatting.
  /// </summary>
  public object ResolveValue( string value )
  {
    return ResolveValue(value, new Stack<string>());
  }

  /// <summary>
  /// Resolves all variables and expressions in text using invariant formatting.
  /// </summary>
  public string ResolveVariables( string? text )
  {
    if (string.IsNullOrEmpty(text)) {
      return string.Empty;
    }
    return ResolveString(text, new Stack<string>());
  }

  /// <summary>
  /// Formats a typed value for use inside URLs, headers, raw bodies, and other strings.
  /// </summary>
  public static string FormatInvariant( object? value )
  {
    return value switch {
      null => string.Empty,
      bool boolean => boolean ? "true" : "false",
      DateTimeOffset dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
      DateTime dateTime => FormatDateTime(dateTime),
      TimeSpan duration => duration.ToString("c", CultureInfo.InvariantCulture),
      Guid guid => guid.ToString("D"),
      IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
      _ => value.ToString() ?? string.Empty
    };
  }

  /// <summary>
  /// Creates a snapshot of the current variable state for debugging.
  /// </summary>
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
  /// Clears all variables.
  /// </summary>
  public void Clear()
  {
    _capturedVariables.Clear();
    _fileVariables.Clear();
    _includedVariables.Clear();
    _resolvedVariables.Clear();
    _variableDependencies.Clear();
  }

  /// <summary>
  /// Creates an independent copy with the same variables, cached values, and UTC clock snapshot.
  /// </summary>
  public VariableStore Clone()
  {
    var clone = new VariableStore(_utcNow);

    foreach (var (key, value) in _includedVariables) {
      clone._includedVariables[key] = value;
    }
    foreach (var (key, value) in _fileVariables) {
      clone._fileVariables[key] = value;
    }
    foreach (var (key, value) in _capturedVariables) {
      clone._capturedVariables[key] = value;
    }
    foreach (var (key, value) in _resolvedVariables) {
      clone._resolvedVariables[key] = value;
    }
    foreach (var (key, dependencies) in _variableDependencies) {
      clone._variableDependencies[key] = new HashSet<string>(dependencies, StringComparer.OrdinalIgnoreCase);
    }

    return clone;
  }

  private object? ResolveVariable( string name, Stack<string> resolutionStack )
  {
    if (resolutionStack.TryPeek(out var dependentName)
      && !string.Equals(dependentName, name, StringComparison.OrdinalIgnoreCase)) {
      if (!_variableDependencies.TryGetValue(dependentName, out var dependencies)) {
        dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _variableDependencies[dependentName] = dependencies;
      }
      dependencies.Add(name);
    }

    if (_resolvedVariables.TryGetValue(name, out var resolved)) {
      return resolved;
    }

    if (resolutionStack.Contains(name, StringComparer.OrdinalIgnoreCase)) {
      var cycle = resolutionStack.Reverse().Concat([name]);
      throw new VariableResolutionException(
        $"Cyclic variable reference detected: {string.Join(" -> ", cycle)}");
    }

    if (!TryGetStoredVariable(name, out var rawValue)) {
      return Environment.GetEnvironmentVariable(name);
    }

    resolutionStack.Push(name);
    try {
      _variableDependencies.Remove(name);
      var value = rawValue is string text
        ? ResolveValue(text, resolutionStack)
        : rawValue;
      _resolvedVariables[name] = value;
      return value;
    } finally {
      resolutionStack.Pop();
    }
  }

  private void InvalidateVariables( IEnumerable<string> changedNames )
  {
    var pending = new Queue<string>(changedNames);
    var invalidated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    while (pending.Count > 0) {
      var changedName = pending.Dequeue();
      if (!invalidated.Add(changedName)) {
        continue;
      }

      _resolvedVariables.Remove(changedName);
      foreach (var (variableName, dependencies) in _variableDependencies) {
        if (dependencies.Contains(changedName)) {
          pending.Enqueue(variableName);
        }
      }
    }

    foreach (var variableName in invalidated) {
      _variableDependencies.Remove(variableName);
    }
  }

  private bool TryGetStoredVariable( string name, out object value )
  {
    if (_capturedVariables.TryGetValue(name, out value!)) {
      return true;
    }
    if (_fileVariables.TryGetValue(name, out value!)) {
      return true;
    }
    if (_includedVariables.TryGetValue(name, out value!)) {
      return true;
    }

    value = null!;
    return false;
  }

  private object ResolveValue( string value, Stack<string> resolutionStack )
  {
    var expressionMatch = ExactExpressionPattern.Match(value);
    if (expressionMatch.Success) {
      return EvaluateExpression(expressionMatch.Groups[1].Value, resolutionStack);
    }

    if (string.Equals(value, "$RandomNumber", StringComparison.Ordinal)) {
      return NextRandomNumber();
    }

    var environmentMatch = ExactEnvVariablePattern.Match(value);
    if (environmentMatch.Success) {
      return ResolveEnvironmentVariable(environmentMatch.Groups[1].Value);
    }

    var variableMatch = ExactVariablePattern.Match(value);
    if (variableMatch.Success) {
      var variableName = variableMatch.Groups[1].Value;
      return ResolveVariable(variableName, resolutionStack)
        ?? throw CreateVariableNotFoundException(variableName);
    }

    return ResolveString(value, resolutionStack);
  }

  private object EvaluateExpression( string expression, Stack<string> resolutionStack )
  {
    return _expressionEvaluator.Evaluate(
      expression,
      name => ResolveVariable(name, resolutionStack));
  }

  private string ResolveString( string text, Stack<string> resolutionStack )
  {
    var result = ExpressionPattern.Replace(text, match =>
      FormatInvariant(EvaluateExpression(match.Groups[1].Value, resolutionStack)));

    result = Regex.Replace(result, @"\$RandomNumber", _ => NextRandomNumber());

    result = EnvVariablePattern.Replace(result, match =>
      ResolveEnvironmentVariable(match.Groups[1].Value));

    result = VariablePattern.Replace(result, match =>
    {
      var variableName = match.Groups[1].Value;
      var value = ResolveVariable(variableName, resolutionStack)
        ?? throw CreateVariableNotFoundException(variableName);
      return FormatInvariant(value);
    });

    return result;
  }

  private static string ResolveEnvironmentVariable( string name )
  {
    return Environment.GetEnvironmentVariable(name)
      ?? throw new VariableNotFoundException($"Environment variable '{name}' not found");
  }

  private static string NextRandomNumber()
  {
    return _randomGenerator.Next(10000, 99999).ToString(CultureInfo.InvariantCulture);
  }

  private VariableNotFoundException CreateVariableNotFoundException( string name )
  {
    return new VariableNotFoundException(
      $"Variable '{name}' not found. Available variables: {string.Join(", ", AllVariableNames)}");
  }

  private static string FormatDateTime( DateTime dateTime )
  {
    var utc = dateTime.Kind switch {
      DateTimeKind.Utc => dateTime,
      DateTimeKind.Local => dateTime.ToUniversalTime(),
      _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
    };
    return utc.ToString("O", CultureInfo.InvariantCulture);
  }
}

/// <summary>
/// Thrown when a variable cannot be found.
/// </summary>
public class VariableNotFoundException : Exception
{
  public VariableNotFoundException( string message ) : base(message) { }
  public VariableNotFoundException( string message, Exception innerException ) : base(message, innerException) { }
}

/// <summary>
/// Thrown when variable dependencies cannot be resolved.
/// </summary>
public class VariableResolutionException : Exception
{
  public VariableResolutionException( string message ) : base(message) { }
  public VariableResolutionException( string message, Exception innerException ) : base(message, innerException) { }
}
