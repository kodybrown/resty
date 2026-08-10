namespace Resty.Core.Parsers;

using System.Globalization;
using Resty.Core.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

/// <summary>
/// Parses expect.status as either one integer or a sequence of integers.
/// </summary>
public sealed class ExpectedStatusCodesConverter : IYamlTypeConverter
{
  public bool Accepts( Type type )
  {
    return type == typeof(ExpectedStatusCodes);
  }

  public object? ReadYaml( IParser parser, Type type, ObjectDeserializer deserializer )
  {
    return ReadCodes(parser, "expect.status");
  }

  internal static ExpectedStatusCodes? ReadCodes( IParser parser, string fieldName )
  {
    var codes = new List<int>();

    if (parser.Current is Scalar scalar) {
      if (string.IsNullOrWhiteSpace(scalar.Value)) {
        parser.MoveNext();
        return null;
      }

      codes.Add(ParseStatusCode(scalar, fieldName));
      parser.MoveNext();
    } else if (parser.Current is SequenceStart) {
      parser.MoveNext();
      while (parser.Current is not SequenceEnd) {
        if (parser.Current is not Scalar item) {
          throw new YamlException($"{fieldName} entries must be integers.");
        }

        codes.Add(ParseStatusCode(item, fieldName));
        parser.MoveNext();
      }
      parser.MoveNext();
    } else {
      throw new YamlException($"{fieldName} must be an integer or a list of integers.");
    }

    if (codes.Count == 0) {
      throw new YamlException($"{fieldName} must contain at least one integer.");
    }

    return new ExpectedStatusCodes(codes);
  }

  public void WriteYaml( IEmitter emitter, object? value, Type type, ObjectSerializer serializer )
  {
    if (value is not ExpectedStatusCodes expected) {
      emitter.Emit(new Scalar(null, null, string.Empty, ScalarStyle.Plain, true, false));
      return;
    }

    if (expected.Codes.Count == 1) {
      emitter.Emit(new Scalar(expected.Codes[0].ToString(CultureInfo.InvariantCulture)));
      return;
    }

    emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Flow));
    foreach (var code in expected.Codes) {
      emitter.Emit(new Scalar(code.ToString(CultureInfo.InvariantCulture)));
    }
    emitter.Emit(new SequenceEnd());
  }

  private static int ParseStatusCode( Scalar scalar, string fieldName )
  {
    if (!int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)) {
      throw new YamlException($"{fieldName} value '{scalar.Value}' is not an integer.");
    }

    return code;
  }
}
