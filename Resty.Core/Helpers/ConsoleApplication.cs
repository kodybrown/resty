//
// Copyright (C) 2013-2020 Kody Brown (kody@bricksoft.com).
//
// MIT License:
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

namespace Resty.Helpers;

using System.Globalization;

public class ConsoleApplication
{
  public bool OptHelp { get; set; } = false;
  public string? OptHelpTopic { get; set; } = null;
  public bool OptVersion { get; set; } = false;
  public bool OptVersionFull { get; set; } = false;
  public bool OptPause { get; set; } = false;
  public bool OptVerbose { get; set; } = false;

  protected void PauseFlagHandler()
  {
    if (OptPause) {
      Console.Write("Press any key to exit: ");
      Console.ReadKey(true);
      Console.CursorLeft = 0;
      Console.Write("                       ");
      Console.CursorLeft = 0;
    }
  }

  protected (int Code, string[] UnhandledArguments) ParseCommonArguments( string[] arguments )
  {
    var leftoverArgs = new List<string>();

    for (var i = 0; i < arguments.Length; i++) {
      var arg = arguments[i];
      var isFlag = false;
      var flagVal = true;

      while (arg.StartsWith('-') || arg.StartsWith('/')) {
        isFlag = true;
        arg = arg[1..];
      }

      if (isFlag && arg.StartsWith('!')) {
        flagVal = false;
        arg = arg[1..];
      }

      if (isFlag) {
        var argLower = arg.ToLower();

        switch (argLower) {
          case "help" or "h" or "?":
            OptHelp = flagVal;
            i = GetSubArgument(arguments, i, out var found, out var value);
            if (found) {
              OptHelpTopic = value;
            }
            continue;

          case "version":
            OptVersionFull = flagVal;
            continue;
          case "ver" or "v":
            OptVersion = flagVal;
            continue;

          case "pause" or "p":
            OptPause = flagVal;
            continue;

          case "verbose" or "e":
            OptVerbose = flagVal;
            continue;
        }
      } else {
        var argLower = arg.ToLower();

        switch (argLower) {
          case "help" or "h" or "?":
            OptHelp = flagVal;
            i = GetSubArgument(arguments, i, out var found, out var value);
            if (found) {
              OptHelpTopic = value;
            }
            continue;

          case "version":
            OptVersionFull = flagVal;
            continue;
        }
      }

      leftoverArgs.Add(arguments[i]);
    }

    // Check the common flags.
    //if (OptHelp) {
    //	ShowHelp();
    //	return -1;
    //} else if (OptVersionFull) {
    //	ShowVersion(true);
    //	return -1;
    //} else if (OptVersion) {
    //	ShowVersion(false);
    //	return -1;
    //}

    return (0, leftoverArgs.ToArray());
  }

  protected int GetSubArgument( string[] arguments, int i, out bool found, out string? result, bool ignoreFlagSymbols = false )
  {
    if (arguments is null) {
      throw new ArgumentNullException(nameof(arguments));
    }

    found = false;
    result = null;

    if (i < arguments.Length - 1) {
      if (ignoreFlagSymbols || (!arguments[i + 1].StartsWith("-") && !arguments[i + 1].StartsWith("/"))) {
        found = true;
        result = arguments[++i];
      }
    }

    return i;
  }

  protected int GetSubArgument<T>( string[] arguments, int index, out bool found, out T? result, bool ignoreFlagSymbols = false )
  {
    if (arguments is null || arguments.Length == 0) {
      throw new ArgumentNullException(nameof(arguments));
    }

    string? subItem = null;

    if (index < arguments.Length - 1) {
      if (ignoreFlagSymbols || (!arguments[index + 1].StartsWith("-") && !arguments[index + 1].StartsWith("/"))) {
        subItem = arguments[index + 1];
      }
    }

    if (subItem != null) {
      index++;
      found = true;

      if (typeof(T) == typeof(string)) {
        result = (T)(object)subItem;
        return index;
      } else if (typeof(T) == typeof(bool)) {
        result = subItem switch {
          "true" or "t" or "yes" or "1" => (T)(object)true,
          _ => (T)(object)false,
        };
        return index;
      } else if (typeof(T) == typeof(DateTime)) {
        if (subItem.ToLower() == "now" || subItem.ToLower() == "utcnow") {
          result = (T)(object)DateTime.UtcNow;
          return index;
        }
        if (DateTime.TryParse(subItem, null, DateTimeStyles.AssumeUniversal, out var val)) {
          if (val.Kind != DateTimeKind.Utc) {
            val = val.ToUniversalTime();
          }
          result = (T)(object)val;
          return index;
        }
      } else if (typeof(T) == typeof(TimeSpan)) {
        if (TimeSpan.TryParse(subItem, out var val)) {
          result = (T)(object)val;
          return index;
        }
      } else if (typeof(T) == typeof(short)) {
        if (short.TryParse(subItem, out var val)) {
          result = (T)(object)val;
          return index;
        }
      } else if (typeof(T) == typeof(int)) {
        if (int.TryParse(subItem, out var val)) {
          result = (T)(object)val;
          return index;
        }
      } else if (typeof(T) == typeof(long)) {
        if (long.TryParse(subItem, out var val)) {
          result = (T)(object)val;
          return index;
        }
      } else if (typeof(T) == typeof(ushort)) {
        if (ushort.TryParse(subItem, out var val)) {
          result = (T)(object)val;
          return index;
        }
      } else if (typeof(T) == typeof(uint)) {
        if (uint.TryParse(subItem, out var val)) {
          result = (T)(object)val;
          return index;
        }
      } else if (typeof(T) == typeof(ulong)) {
        if (ulong.TryParse(subItem, out var val)) {
          result = (T)(object)val;
          return index;
        }
      } else if (typeof(T) == typeof(float)) {
        if (float.TryParse(subItem, out var val)) {
          result = (T)(object)val;
          return index;
        }
      } else if (typeof(T) == typeof(double)) {
        if (double.TryParse(subItem, out var val)) {
          result = (T)(object)val;
          return index;
        }
      } else if (typeof(T) == typeof(decimal)) {
        if (decimal.TryParse(subItem, out var val)) {
          result = (T)(object)val;
          return index;
        }
      } else {
        throw new NotSupportedException(nameof(T));
      }
    }

    if (typeof(T) == typeof(bool)) {
      result = (T)(object)true;
      found = true;
    } else {
      result = default(T);
      found = false;
    }

    return index;
  }

  protected int GetSubArguments( string[] arguments, int i, out bool found, List<string> items, bool ignoreFlagSymbols = false )
  {
    if (arguments is null) {
      throw new ArgumentNullException(nameof(arguments));
    }
    if (items is null) {
      throw new ArgumentNullException(nameof(items));
    }

    found = false;

    while (i < arguments.Length - 1) {
      if (!ignoreFlagSymbols && (arguments[i + 1].StartsWith("-") || arguments[i + 1].StartsWith("/"))) {
        break;
      }
      if (string.IsNullOrWhiteSpace(arguments[i + 1])) {
        i++;
        continue;
      }

      found = true;
      items.Add(arguments[++i]);
    }

    return i;
  }

  protected int GetSubArguments<T>( string[] arguments, int i, out bool found, List<T> items, bool ignoreFlagSymbols = false )
  {
    if (arguments is null) {
      throw new ArgumentNullException(nameof(arguments));
    }
    if (items is null) {
      throw new ArgumentNullException(nameof(items));
    }

    found = false;

    while (i < arguments.Length - 1) {
      if (!ignoreFlagSymbols && (arguments[i + 1].StartsWith("-") || arguments[i + 1].StartsWith("/"))) {
        break;
      }
      if (string.IsNullOrWhiteSpace(arguments[i + 1])) {
        i++;
        continue;
      }

      i = GetSubArgument<T>(arguments, i, out found, out var result);
      if (found && result != null) {
        items.Add(result);
      }
    }

    return i;
  }
}
