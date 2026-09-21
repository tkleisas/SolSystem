using System.Globalization;

namespace SolSystem.Probe;

/// <summary>
/// Something a probe command could not do: an unknown verb, a missing or unparseable
/// argument, a body that is not in the system.
/// </summary>
/// <remarks>
/// A distinct type because a command that fails must not abort the script. The runner
/// catches this, writes an <c>error:</c> line and runs the next command, so a typo on line
/// four of a fifty-line script costs one line rather than the other forty-six.
/// </remarks>
internal sealed class ProbeException : Exception
{
    internal ProbeException(string message)
        : base(message)
    {
    }
}

/// <summary>One line of a probe script, already split into a verb and its arguments.</summary>
internal sealed class ProbeCommand
{
    internal ProbeCommand(int line, string text, string verb, string[] arguments)
    {
        Line = line;
        Text = text;
        Verb = verb;
        Arguments = arguments;
    }

    /// <summary>Line number in the script, for errors that can be acted on.</summary>
    internal int Line { get; }

    /// <summary>The line as written, echoed into the transcript so it stays navigable.</summary>
    internal string Text { get; }

    /// <summary>The command word, matched case-insensitively.</summary>
    internal string Verb { get; }

    /// <summary>Everything after the verb.</summary>
    internal string[] Arguments { get; }

    internal int ArgumentCount => Arguments.Length;

    /// <summary>A required argument.</summary>
    internal string Argument(int index, string expected, string usage)
    {
        if (index >= Arguments.Length)
        {
            throw new ProbeException($"'{Verb}' needs {expected} — usage: {usage}");
        }

        return Arguments[index];
    }

    /// <summary>A required whole number.</summary>
    internal int Whole(int index, string expected, string usage, int min = int.MinValue,
        int max = int.MaxValue)
    {
        string raw = Argument(index, expected, usage);
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new ProbeException($"'{Verb}' expected a whole number for {expected}, got '{raw}'");
        }

        if (value < min || value > max)
        {
            throw new ProbeException($"'{Verb}' wants {expected} between {min} and {max}, got {value}");
        }

        return value;
    }

    /// <summary>A required real number. Parsed invariantly, so a script reads the same anywhere.</summary>
    internal double Real(int index, string expected, string usage)
    {
        string raw = Argument(index, expected, usage);
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new ProbeException($"'{Verb}' expected a number for {expected}, got '{raw}'");
        }

        return value;
    }

    /// <summary>An optional real number, with a default.</summary>
    internal double RealOr(int index, double fallback)
    {
        if (index >= Arguments.Length)
        {
            return fallback;
        }

        return double.Parse(Arguments[index], NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

/// <summary>A probe script: a list of commands, in order, with their source lines.</summary>
/// <remarks>
/// Deliberately a text format rather than a data one. A probe is a question asked of the
/// simulation, and the question is easier to check than the answer: a script a person can
/// read top to bottom is one whose mistakes are visible, and these files are meant to be
/// read by whoever is looking at a failed transcript six months from now.
/// </remarks>
internal sealed class ProbeScript
{
    private readonly List<ProbeCommand> _commands = new();

    private ProbeScript(string name)
    {
        Name = name;
    }

    /// <summary>The file name, for the transcript header.</summary>
    internal string Name { get; }

    internal IReadOnlyList<ProbeCommand> Commands => _commands;

    /// <summary>Reads a script from a file.</summary>
    internal static ProbeScript Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new ProbeException($"no such probe script: {path}");
        }

        return Parse(Path.GetFileName(path), File.ReadAllText(path));
    }

    /// <summary>Parses a script from text.</summary>
    internal static ProbeScript Parse(string name, string text)
    {
        var script = new ProbeScript(name);
        string[] lines = text.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            // A comment runs to the end of the line, so a probe can explain itself beside
            // the command it explains rather than in a block at the top.
            int comment = line.IndexOf('#');
            if (comment >= 0)
            {
                line = line.Substring(0, comment);
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = Split(line);
            script._commands.Add(new ProbeCommand(
                i + 1,
                lines[i].TrimEnd(),
                parts[0].ToLowerInvariant(),
                parts.Skip(1).ToArray()));
        }

        return script;
    }

    /// <summary>
    /// Splits a line into arguments, honouring double quotes.
    /// </summary>
    /// <remarks>
    /// A check's label is free text and free text has spaces in it, so
    /// <c>expect "the ship is slow enough" closing 0.5 less</c> has to arrive as four
    /// arguments and not seven. Without this the label silently became the value, and the
    /// probe reported a parse error on a line that reads perfectly well.
    /// </remarks>
    private static string[] Split(string line)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;

        foreach (char c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts.ToArray();
    }
}
