namespace Resty.Core.Variables;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Evaluates Resty's bounded expression language without executing arbitrary code.
/// </summary>
public sealed class ExpressionEvaluator
{
  private readonly DateTimeOffset _utcNow;

  public ExpressionEvaluator( DateTimeOffset utcNow )
  {
    _utcNow = utcNow.ToUniversalTime();
  }

  /// <summary>
  /// Evaluates an expression using the provided variable resolver.
  /// </summary>
  public object Evaluate( string expression, Func<string, object?> variableResolver )
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(expression);
    ArgumentNullException.ThrowIfNull(variableResolver);

    var parser = new Parser(expression, variableResolver, _utcNow);
    return parser.Parse();
  }

  private sealed class Parser
  {
    private const int MaximumRandomHexLength = 1024;

    private readonly string _expression;
    private readonly Func<string, object?> _variableResolver;
    private readonly DateTimeOffset _utcNow;
    private int _position;
    private Token _current;

    public Parser(
        string expression,
        Func<string, object?> variableResolver,
        DateTimeOffset utcNow )
    {
      _expression = expression;
      _variableResolver = variableResolver;
      _utcNow = utcNow;
      _current = ReadToken();
    }

    public object Parse()
    {
      var value = ParseOr();
      Expect(TokenType.End, "Unexpected token after the end of the expression");
      return value;
    }

    private object ParseOr()
    {
      var left = ParseAnd();
      while (Match(TokenType.OrOr) || MatchKeyword("or")) {
        var right = ParseAnd();
        left = RequireBoolean(left, "or") || RequireBoolean(right, "or");
      }
      return left;
    }

    private object ParseAnd()
    {
      var left = ParseEquality();
      while (Match(TokenType.AndAnd) || MatchKeyword("and")) {
        var right = ParseEquality();
        left = RequireBoolean(left, "and") && RequireBoolean(right, "and");
      }
      return left;
    }

    private object ParseEquality()
    {
      var left = ParseComparison();
      while (_current.Type is TokenType.EqualEqual or TokenType.NotEqual) {
        var operatorType = _current.Type;
        Advance();
        var right = ParseComparison();
        var equal = ValuesEqual(left, right);
        left = operatorType == TokenType.EqualEqual ? equal : !equal;
      }
      return left;
    }

    private object ParseComparison()
    {
      var left = ParseAdditive();
      while (_current.Type is TokenType.LessThan
        or TokenType.LessThanOrEqual
        or TokenType.GreaterThan
        or TokenType.GreaterThanOrEqual) {
        var operatorType = _current.Type;
        Advance();
        var right = ParseAdditive();
        var comparison = CompareValues(left, right);
        left = operatorType switch {
          TokenType.LessThan => comparison < 0,
          TokenType.LessThanOrEqual => comparison <= 0,
          TokenType.GreaterThan => comparison > 0,
          TokenType.GreaterThanOrEqual => comparison >= 0,
          _ => throw new InvalidOperationException("Unsupported comparison operator")
        };
      }
      return left;
    }

    private object ParseAdditive()
    {
      var left = ParseMultiplicative();
      while (_current.Type is TokenType.Plus or TokenType.Minus) {
        var operatorType = _current.Type;
        Advance();
        var right = ParseMultiplicative();
        left = operatorType == TokenType.Plus
          ? Add(left, right)
          : Subtract(left, right);
      }
      return left;
    }

    private object ParseMultiplicative()
    {
      var left = ParseUnary();
      while (_current.Type is TokenType.Star or TokenType.Slash or TokenType.Percent) {
        var operatorType = _current.Type;
        Advance();
        var right = ParseUnary();
        left = operatorType switch {
          TokenType.Star => Multiply(left, right),
          TokenType.Slash => Divide(left, right),
          TokenType.Percent => Modulo(left, right),
          _ => throw new InvalidOperationException("Unsupported multiplicative operator")
        };
      }
      return left;
    }

    private object ParseUnary()
    {
      if (Match(TokenType.Bang) || MatchKeyword("not")) {
        return !RequireBoolean(ParseUnary(), "not");
      }
      if (Match(TokenType.Minus)) {
        return Negate(ParseUnary());
      }
      if (Match(TokenType.Plus)) {
        var value = ParseUnary();
        RequireNumeric(value, "+");
        return value;
      }
      return ParsePrimary();
    }

    private object ParsePrimary()
    {
      if (_current.Type == TokenType.Number) {
        var token = _current;
        Advance();
        return ParseNumber(token);
      }

      if (_current.Type == TokenType.String) {
        var value = _current.Text;
        Advance();
        return value;
      }

      if (_current.Type == TokenType.Identifier) {
        var name = _current.Text;
        Advance();

        if (string.Equals(name, "true", StringComparison.OrdinalIgnoreCase)) {
          return true;
        }
        if (string.Equals(name, "false", StringComparison.OrdinalIgnoreCase)) {
          return false;
        }

        if (Match(TokenType.LeftParenthesis)) {
          return InvokeFunction(name, ParseArguments());
        }

        return _variableResolver(name)
          ?? throw Error($"Variable '{name}' was not found");
      }

      if (Match(TokenType.LeftParenthesis)) {
        var value = ParseOr();
        Expect(TokenType.RightParenthesis, "Expected ')' after expression");
        Advance();
        return value;
      }

      throw Error($"Expected a value but found '{_current.Text}'");
    }

    private List<object> ParseArguments()
    {
      var arguments = new List<object>();
      if (Match(TokenType.RightParenthesis)) {
        return arguments;
      }

      do {
        arguments.Add(ParseOr());
      } while (Match(TokenType.Comma));

      Expect(TokenType.RightParenthesis, "Expected ')' after function arguments");
      Advance();
      return arguments;
    }

    private object InvokeFunction( string name, List<object> arguments )
    {
      switch (name.ToLowerInvariant()) {
        case "utc_now":
          RequireArgumentCount(name, arguments, 0);
          return _utcNow;

        case "start_of_hour":
          RequireArgumentCount(name, arguments, 1);
          var hour = RequireDateTime(arguments[0], name).ToUniversalTime();
          return new DateTimeOffset(hour.Year, hour.Month, hour.Day, hour.Hour, 0, 0, TimeSpan.Zero);

        case "milliseconds":
          RequireArgumentCount(name, arguments, 1);
          return TimeSpan.FromMilliseconds(ToDouble(arguments[0], name));

        case "seconds":
          RequireArgumentCount(name, arguments, 1);
          return TimeSpan.FromSeconds(ToDouble(arguments[0], name));

        case "minutes":
          RequireArgumentCount(name, arguments, 1);
          return TimeSpan.FromMinutes(ToDouble(arguments[0], name));

        case "hours":
          RequireArgumentCount(name, arguments, 1);
          return TimeSpan.FromHours(ToDouble(arguments[0], name));

        case "days":
          RequireArgumentCount(name, arguments, 1);
          return TimeSpan.FromDays(ToDouble(arguments[0], name));

        case "unix_seconds":
          RequireArgumentCount(name, arguments, 1);
          return RequireDateTime(arguments[0], name).ToUnixTimeSeconds();

        case "unix_milliseconds":
          RequireArgumentCount(name, arguments, 1);
          return RequireDateTime(arguments[0], name).ToUnixTimeMilliseconds();

        case "format":
          RequireArgumentCount(name, arguments, 2);
          if (arguments[1] is not string format) {
            throw Error("format() requires its second argument to be a string");
          }
          return Format(arguments[0], format);

        case "uuid":
          RequireArgumentCount(name, arguments, 0);
          return Guid.NewGuid();

        case "random_int":
          return RandomInt(name, arguments);

        case "random_hex":
          RequireArgumentCount(name, arguments, 1);
          return RandomHex(RequireInt32(arguments[0], name));

        default:
          throw Error($"Unknown function '{name}'");
      }
    }

    private object RandomInt( string name, List<object> arguments )
    {
      if (arguments.Count == 0) {
        return RandomNumberGenerator.GetInt32(int.MaxValue);
      }
      if (arguments.Count == 1) {
        var maximum = RequireInt32(arguments[0], name);
        if (maximum <= 0) {
          throw Error("random_int(max) requires max to be greater than zero");
        }
        return RandomNumberGenerator.GetInt32(maximum);
      }
      if (arguments.Count == 2) {
        var minimum = RequireInt32(arguments[0], name);
        var maximum = RequireInt32(arguments[1], name);
        if (minimum >= maximum) {
          throw Error("random_int(min, max) requires min to be less than max");
        }
        return RandomNumberGenerator.GetInt32(minimum, maximum);
      }

      throw Error("random_int() accepts zero, one, or two arguments");
    }

    private string RandomHex( int length )
    {
      if (length < 1 || length > MaximumRandomHexLength) {
        throw Error($"random_hex(length) requires a length from 1 to {MaximumRandomHexLength}");
      }

      var bytes = new byte[(length + 1) / 2];
      RandomNumberGenerator.Fill(bytes);
      return Convert.ToHexString(bytes)[..length];
    }

    private static object Add( object left, object right )
    {
      if (TryGetDateTime(left, out var leftDate) && right is TimeSpan rightDuration) {
        return leftDate + rightDuration;
      }
      if (left is TimeSpan leftDuration && TryGetDateTime(right, out var rightDate)) {
        return rightDate + leftDuration;
      }
      if (left is TimeSpan firstDuration && right is TimeSpan secondDuration) {
        return firstDuration + secondDuration;
      }
      return NumericBinary(left, right, "+", ( a, b ) => a + b, preserveInteger: true);
    }

    private static object Subtract( object left, object right )
    {
      if (TryGetDateTime(left, out var leftDate) && right is TimeSpan duration) {
        return leftDate - duration;
      }
      if (TryGetDateTime(left, out leftDate) && TryGetDateTime(right, out var rightDate)) {
        return leftDate - rightDate;
      }
      if (left is TimeSpan firstDuration && right is TimeSpan secondDuration) {
        return firstDuration - secondDuration;
      }
      return NumericBinary(left, right, "-", ( a, b ) => a - b, preserveInteger: true);
    }

    private static object Multiply( object left, object right )
    {
      return NumericBinary(left, right, "*", ( a, b ) => a * b, preserveInteger: true);
    }

    private object Divide( object left, object right )
    {
      var divisor = RequireNumeric(right, "/");
      if (divisor == 0) {
        throw Error("Division by zero is not allowed");
      }
      return RequireNumeric(left, "/") / divisor;
    }

    private object Modulo( object left, object right )
    {
      var divisor = RequireNumeric(right, "%");
      if (divisor == 0) {
        throw Error("Division by zero is not allowed");
      }
      return CreateNumericResult(
        RequireNumeric(left, "%") % divisor,
        IsIntegral(left) && IsIntegral(right));
    }

    private static object Negate( object value )
    {
      return CreateNumericResult(-RequireNumeric(value, "unary -"), IsIntegral(value));
    }

    private static object NumericBinary(
        object left,
        object right,
        string operatorName,
        Func<decimal, decimal, decimal> operation,
        bool preserveInteger )
    {
      var result = operation(
        RequireNumeric(left, operatorName),
        RequireNumeric(right, operatorName));
      return CreateNumericResult(result, preserveInteger && IsIntegral(left) && IsIntegral(right));
    }

    private static object CreateNumericResult( decimal value, bool preserveInteger )
    {
      if (preserveInteger
        && value >= long.MinValue
        && value <= long.MaxValue
        && value == decimal.Truncate(value)) {
        return decimal.ToInt64(value);
      }
      return value;
    }

    private static bool ValuesEqual( object left, object right )
    {
      if (IsNumeric(left) && IsNumeric(right)) {
        return RequireNumeric(left, "==") == RequireNumeric(right, "==");
      }
      if (TryGetDateTime(left, out var leftDate) && TryGetDateTime(right, out var rightDate)) {
        return leftDate.Equals(rightDate);
      }
      return Equals(left, right);
    }

    private static int CompareValues( object left, object right )
    {
      if (IsNumeric(left) && IsNumeric(right)) {
        return RequireNumeric(left, "comparison").CompareTo(RequireNumeric(right, "comparison"));
      }
      if (TryGetDateTime(left, out var leftDate) && TryGetDateTime(right, out var rightDate)) {
        return leftDate.CompareTo(rightDate);
      }
      if (left is TimeSpan leftDuration && right is TimeSpan rightDuration) {
        return leftDuration.CompareTo(rightDuration);
      }
      throw new ExpressionEvaluationException(
        $"Comparison requires numbers, date/time values, or durations; got {TypeName(left)} and {TypeName(right)}");
    }

    private static decimal RequireNumeric( object value, string operation )
    {
      try {
        return value switch {
          byte number => number,
          sbyte number => number,
          short number => number,
          ushort number => number,
          int number => number,
          uint number => number,
          long number => number,
          ulong number => number,
          float number when float.IsFinite(number) => Convert.ToDecimal(number, CultureInfo.InvariantCulture),
          double number when double.IsFinite(number) => Convert.ToDecimal(number, CultureInfo.InvariantCulture),
          decimal number => number,
          _ => throw new ExpressionEvaluationException(
            $"Operator '{operation}' requires numeric operands; got {TypeName(value)}")
        };
      } catch (OverflowException ex) {
        throw new ExpressionEvaluationException(
          $"Numeric value for operator '{operation}' is outside the supported decimal range", ex);
      }
    }

    private static bool RequireBoolean( object value, string operation )
    {
      return value is bool boolean
        ? boolean
        : throw new ExpressionEvaluationException(
          $"Operator '{operation}' requires boolean operands; got {TypeName(value)}");
    }

    private static DateTimeOffset RequireDateTime( object value, string functionName )
    {
      return TryGetDateTime(value, out var dateTime)
        ? dateTime
        : throw new ExpressionEvaluationException(
          $"{functionName}() requires a date/time argument; got {TypeName(value)}");
    }

    private static bool TryGetDateTime( object value, out DateTimeOffset dateTime )
    {
      switch (value) {
        case DateTimeOffset offset:
          dateTime = offset.ToUniversalTime();
          return true;
        case DateTime date:
          dateTime = date.Kind switch {
            DateTimeKind.Utc => new DateTimeOffset(date, TimeSpan.Zero),
            DateTimeKind.Local => new DateTimeOffset(date).ToUniversalTime(),
            _ => new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc), TimeSpan.Zero)
          };
          return true;
        default:
          dateTime = default;
          return false;
      }
    }

    private static bool IsNumeric( object value )
    {
      return value is byte or sbyte or short or ushort or int or uint or long or ulong
        or float or double or decimal;
    }

    private static bool IsIntegral( object value )
    {
      return value is byte or sbyte or short or ushort or int or uint or long or ulong;
    }

    private static double ToDouble( object value, string functionName )
    {
      return decimal.ToDouble(RequireNumeric(value, functionName));
    }

    private int RequireInt32( object value, string functionName )
    {
      var number = RequireNumeric(value, functionName);
      if (number != decimal.Truncate(number) || number < int.MinValue || number > int.MaxValue) {
        throw Error($"{functionName}() requires a 32-bit integer argument");
      }
      return decimal.ToInt32(number);
    }

    private static string Format( object value, string format )
    {
      return value switch {
        DateTimeOffset dateTime => dateTime.ToString(format, CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString(format, CultureInfo.InvariantCulture),
        TimeSpan duration => duration.ToString(format, CultureInfo.InvariantCulture),
        Guid guid => guid.ToString(string.IsNullOrEmpty(format) ? "D" : format),
        IFormattable formattable => formattable.ToString(format, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
      };
    }

    private static void RequireArgumentCount( string name, List<object> arguments, int expected )
    {
      if (arguments.Count != expected) {
        throw new ExpressionEvaluationException(
          $"{name}() requires {expected} argument{(expected == 1 ? string.Empty : "s")}; received {arguments.Count}");
      }
    }

    private static object ParseNumber( Token token )
    {
      if (!token.Text.AsSpan().ContainsAny('.', 'e', 'E')
        && long.TryParse(token.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) {
        return integer;
      }
      if (decimal.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) {
        return number;
      }
      throw new ExpressionEvaluationException($"Invalid numeric literal '{token.Text}' at position {token.Position}");
    }

    private bool Match( TokenType type )
    {
      if (_current.Type != type) {
        return false;
      }
      Advance();
      return true;
    }

    private bool MatchKeyword( string keyword )
    {
      if (_current.Type != TokenType.Identifier
        || !string.Equals(_current.Text, keyword, StringComparison.OrdinalIgnoreCase)) {
        return false;
      }
      Advance();
      return true;
    }

    private void Expect( TokenType type, string message )
    {
      if (_current.Type != type) {
        throw Error(message);
      }
    }

    private void Advance()
    {
      _current = ReadToken();
    }

    private Token ReadToken()
    {
      while (_position < _expression.Length && char.IsWhiteSpace(_expression[_position])) {
        _position++;
      }

      if (_position >= _expression.Length) {
        return new Token(TokenType.End, string.Empty, _position);
      }

      var start = _position;
      var character = _expression[_position++];
      switch (character) {
        case '+':
          return new Token(TokenType.Plus, "+", start);
        case '-':
          return new Token(TokenType.Minus, "-", start);
        case '*':
          return new Token(TokenType.Star, "*", start);
        case '/':
          return new Token(TokenType.Slash, "/", start);
        case '%':
          return new Token(TokenType.Percent, "%", start);
        case '(':
          return new Token(TokenType.LeftParenthesis, "(", start);
        case ')':
          return new Token(TokenType.RightParenthesis, ")", start);
        case ',':
          return new Token(TokenType.Comma, ",", start);
        case '!':
          return MatchCharacter('=')
            ? new Token(TokenType.NotEqual, "!=", start)
            : new Token(TokenType.Bang, "!", start);
        case '=':
          if (MatchCharacter('=')) { return new Token(TokenType.EqualEqual, "==", start); }
          throw Error("Use '==' for equality comparisons", start);
        case '<':
          return MatchCharacter('=')
            ? new Token(TokenType.LessThanOrEqual, "<=", start)
            : new Token(TokenType.LessThan, "<", start);
        case '>':
          return MatchCharacter('=')
            ? new Token(TokenType.GreaterThanOrEqual, ">=", start)
            : new Token(TokenType.GreaterThan, ">", start);
        case '&':
          if (MatchCharacter('&')) { return new Token(TokenType.AndAnd, "&&", start); }
          throw Error("Use '&&' for boolean and", start);
        case '|':
          if (MatchCharacter('|')) { return new Token(TokenType.OrOr, "||", start); }
          throw Error("Use '||' for boolean or", start);
        case '\'':
        case '"':
          return ReadString(character, start);
        case '$':
          if (_position >= _expression.Length || !IsIdentifierStart(_expression[_position])) {
            throw Error("Expected a variable name after '$'", start);
          }
          return ReadIdentifier(start, _position);
        default:
          if (char.IsDigit(character) || (character == '.' && PeekIsDigit())) {
            return ReadNumber(start);
          }
          if (IsIdentifierStart(character)) {
            return ReadIdentifier(start, start);
          }
          throw Error($"Unexpected character '{character}'", start);
      }
    }

    private Token ReadNumber( int start )
    {
      while (_position < _expression.Length && char.IsDigit(_expression[_position])) {
        _position++;
      }

      if (_position < _expression.Length && _expression[_position] == '.') {
        _position++;
        while (_position < _expression.Length && char.IsDigit(_expression[_position])) {
          _position++;
        }
      }

      if (_position < _expression.Length && (_expression[_position] is 'e' or 'E')) {
        _position++;
        if (_position < _expression.Length && (_expression[_position] is '+' or '-')) {
          _position++;
        }
        var exponentStart = _position;
        while (_position < _expression.Length && char.IsDigit(_expression[_position])) {
          _position++;
        }
        if (_position == exponentStart) {
          throw Error("Expected digits after numeric exponent", exponentStart);
        }
      }

      return new Token(TokenType.Number, _expression[start.._position], start);
    }

    private Token ReadIdentifier( int tokenStart, int identifierStart )
    {
      if (identifierStart == _position) {
        _position++;
      }
      while (_position < _expression.Length && IsIdentifierPart(_expression[_position])) {
        _position++;
      }
      return new Token(TokenType.Identifier, _expression[identifierStart.._position], tokenStart);
    }

    private Token ReadString( char quote, int start )
    {
      var result = new StringBuilder();
      while (_position < _expression.Length) {
        var character = _expression[_position++];
        if (character == quote) {
          return new Token(TokenType.String, result.ToString(), start);
        }
        if (character != '\\') {
          result.Append(character);
          continue;
        }
        if (_position >= _expression.Length) {
          throw Error("Unterminated escape sequence in string literal", start);
        }

        var escaped = _expression[_position++];
        result.Append(escaped switch {
          '\\' => '\\',
          '\'' => '\'',
          '"' => '"',
          'n' => '\n',
          'r' => '\r',
          't' => '\t',
          _ => throw Error($"Unsupported escape sequence '\\{escaped}'", _position - 2)
        });
      }
      throw Error("Unterminated string literal", start);
    }

    private bool MatchCharacter( char expected )
    {
      if (_position >= _expression.Length || _expression[_position] != expected) {
        return false;
      }
      _position++;
      return true;
    }

    private bool PeekIsDigit()
    {
      return _position < _expression.Length && char.IsDigit(_expression[_position]);
    }

    private static bool IsIdentifierStart( char character )
    {
      return char.IsLetter(character) || character == '_';
    }

    private static bool IsIdentifierPart( char character )
    {
      return char.IsLetterOrDigit(character) || character == '_';
    }

    private ExpressionEvaluationException Error( string message, int? position = null )
    {
      return new ExpressionEvaluationException(
        $"{message} at position {position ?? _current.Position} in expression '{_expression}'");
    }

    private static string TypeName( object value )
    {
      return value.GetType().Name;
    }
  }

  private readonly record struct Token( TokenType Type, string Text, int Position );

  private enum TokenType
  {
    End,
    Number,
    String,
    Identifier,
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    LeftParenthesis,
    RightParenthesis,
    Comma,
    Bang,
    EqualEqual,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    AndAnd,
    OrOr
  }
}

/// <summary>
/// Thrown when a Resty expression is invalid or cannot be evaluated safely.
/// </summary>
public class ExpressionEvaluationException : Exception
{
  public ExpressionEvaluationException( string message ) : base(message) { }
  public ExpressionEvaluationException( string message, Exception innerException ) : base(message, innerException) { }
}
