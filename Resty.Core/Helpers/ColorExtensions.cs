namespace Resty.Core.Helpers;

using System;
using System.Text.RegularExpressions;

public record ColorDefinition( string Code, string ProperName, ConsoleColor Color );

public static class ColorExtensions
{
  public static List<ColorDefinition> ColorDefinitions
    => [
      new ("white", "White", ConsoleColor.White),
      new ("black", "Black", ConsoleColor.Black),
      new ("blue", "Blue", ConsoleColor.Blue),
      new ("cyan", "Cyan", ConsoleColor.Cyan),
      new ("gray", "Gray", ConsoleColor.Gray),
      new ("green", "Green", ConsoleColor.Green),
      new ("magenta", "Magenta", ConsoleColor.Magenta),
      new ("red", "Red", ConsoleColor.Red),
      new ("yellow", "Yellow", ConsoleColor.Yellow),
      new ("darkblue", "DarkBlue", ConsoleColor.DarkBlue),
      new ("darkcyan", "DarkCyan", ConsoleColor.DarkCyan),
      new ("darkgray", "DarkGray", ConsoleColor.DarkGray),
      new ("darkgreen", "DarkGreen", ConsoleColor.DarkGreen),
      new ("darkmagenta", "DarkMagenta", ConsoleColor.DarkMagenta),
      new ("darkred", "DarkRed", ConsoleColor.DarkRed),
      new ("darkyellow", "DarkYellow", ConsoleColor.DarkYellow),
      new ("normal", "Normal", Console.ForegroundColor) // Resets to the default console color,
    ];

  /// <summary> Finds a color definition by its code (string). </summary>
  /// <param name="color"></param>
  /// <returns></returns>
  public static ColorDefinition? FindColor( string? color )
  {
    if (string.IsNullOrWhiteSpace(color)) { return null; }

    // Normalize the 'color' string for matching.
    color = color.Trim().ToLowerInvariant()
                 .Replace(" ", string.Empty)
                 .Replace("-", string.Empty)
                 .Replace("_", string.Empty)
                 .TrimStart('{', '@').TrimEnd('}')
                 .TrimStart('[', '@').TrimEnd(']');

    // Special case for resetting to the normal/default color.
    if (color is "/" or "default" or "normal" or "reset" or "/reset") {
      color = "normal";
    }

    return ColorDefinitions.Find(c => c.Code == color);
  }

  /// <summary> Converts the specified <paramref name="color"/> (string) to a color variable for use in formatted output. </summary>
  /// <param name="color">The color can be in (almost) any format. These are accepted: `green`, `darkgreen`, `dark-green`, `dark_green`, `dark green`, `{@green}`, `[green]`, `[/]`, `Reset`, etc.</param>
  /// <returns></returns>
  public static string? ToColorVariable( this string? color )
  {
    var match = FindColor(color);
    return match is not null
      ? $"{{@{match.Code}}}"
      : null;
  }

  /// <summary> Converts the specified <paramref name="color"/> (ConsoleColor) to a color variable for use in formatted output. </summary>
  /// <param name="color"></param>
  /// <returns></returns>
  public static string ToColorVariable( this ConsoleColor color )
  {
    var match = ColorDefinitions.Find(c => c.Color == color);
    return match is not null
      ? $"{{@{match.Code}}}"
      : null;
  }

  /// <summary> Converts the specified <paramref name="color"/> to a ConsoleColor. </summary>
  /// <param name="color">The color can be in (almost) any format. These are accepted: `green`, `darkgreen`, `dark-green`, `dark_green`, `dark green`, `{@green}`, `[green]`, `[/]`, `Reset`, etc.</param>
  /// <returns></returns>
  public static ConsoleColor ToConsoleColor( this string? color, ConsoleColor defaultColor = ConsoleColor.DarkRed )
  {
    var match = FindColor(color);
    return match?.Color ?? defaultColor;
  }

  private static readonly Regex ColorTagRegex = new(@"\{@[A-Za-z]+\}", RegexOptions.Compiled);

  public static string StripColorVariables( string text ) => !string.IsNullOrEmpty(text) ? ColorTagRegex.Replace(text, string.Empty) : text;

  public static void WriteToConsoleWithColors( string line )
  {
    Console.ResetColor();
    var i = 0;
    while (i < line.Length) {
      if (line[i] == '{' && i + 2 < line.Length && line[i + 1] == '@') {
        var end = line.IndexOf('}', i + 2);
        if (end > i) {
          var tag = line.Substring(i + 2, end - (i + 2)); // e.g., "Red" or "Reset"
          if (string.Equals(tag, "Reset", StringComparison.OrdinalIgnoreCase)) {
            Console.ResetColor();
          } else {
            var color = ColorExtensions.ToConsoleColor(tag, ConsoleColor.DarkRed);
            Console.ForegroundColor = color;
          }
          i = end + 1;
          continue;
        }
      }
      Console.Write(line[i]);
      i++;
    }
    Console.ResetColor();
  }
}
