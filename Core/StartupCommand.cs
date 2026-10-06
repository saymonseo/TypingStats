using System.Text;

namespace TypingStats.Core;

public static class StartupCommand
{
    public static string Build(string executable, string dataDirectory)
    {
        var command = Quote(executable) + " --minimized --data-dir " + Quote(dataDirectory);
        if (command.Length > 260) throw new ArgumentException("Путь программы или данных слишком длинный для автозапуска Windows. Выберите более короткую папку.");
        return command;
    }
    public static string Quote(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument) || argument.IndexOfAny(['\0', '\r', '\n']) >= 0) throw new ArgumentException("Некорректный путь для автозапуска.");
        var result = new StringBuilder("\""); var slashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
