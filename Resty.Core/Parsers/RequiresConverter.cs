namespace Resty.Core.Parsers;

using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

/// <summary>
/// Custom YAML converter that handles the 'requires' field in YamlBlock.
/// Supports both single string format (requires: login) and array format (requires: [login, getToken]).
/// </summary>
public class RequiresConverter : IYamlTypeConverter
{
  public bool Accepts( Type type )
  {
    return type == typeof(List<string>) || type == typeof(List<string>);
  }

  public object? ReadYaml( IParser parser, Type type, ObjectDeserializer deserializer )
  {
    var result = new List<string>();

    if (parser.Current is Scalar scalar) {
      // Single string value: requires: login
      result.Add(scalar.Value);
      parser.MoveNext();
    } else if (parser.Current is SequenceStart) {
      // Array of strings: requires: [login, getToken]
      parser.MoveNext(); // Move past SequenceStart

      while (parser.Current is not SequenceEnd) {
        if (parser.Current is Scalar arrayItem) {
          result.Add(arrayItem.Value);
          parser.MoveNext();
        } else {
          // Skip unexpected elements
          parser.MoveNext();
        }
      }

      parser.MoveNext(); // Move past SequenceEnd
    } else {
      // Handle null or other unexpected cases
      parser.MoveNext();
      return null;
    }

    return result.Count > 0 ? result : null;
  }

  public void WriteYaml( IEmitter emitter, object? value, Type type, ObjectSerializer serializer )
  {
    if (value is List<string> stringList) {
      if (stringList.Count == 1) {
        // Write as single scalar if only one item
        emitter.Emit(new Scalar(stringList[0]));
      } else {
        // Write as sequence for multiple items
        emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block));
        foreach (var item in stringList) {
          emitter.Emit(new Scalar(item));
        }
        emitter.Emit(new SequenceEnd());
      }
    } else {
      emitter.Emit(new Scalar(null, null, string.Empty, ScalarStyle.Plain, true, false));
    }
  }
}
