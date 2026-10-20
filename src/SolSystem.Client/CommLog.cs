using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SolSystem.Client;

/// <summary>
/// The comm log: what the narrator says, printed as it arrives.
/// </summary>
/// <remarks>
/// <para>
/// A terminal block in the corner of the flight view: green text typed character by
/// character behind a blinking block cursor, wrapped at forty characters, six lines
/// visible, the whole block scrolling up a line as a new one pushes past it. The words are
/// exactly what the voice says — the terminal prints the transmission the channel carries,
/// verbatim.
/// </para>
/// <para>
/// <b>The typewriter runs at the line's own spoken pace.</b> The narrator reports each line's
/// duration when the line starts; the terminal uses it to choose characters per second, so the
/// text and the voice begin together and finish together. A mission note with no voice behind it
/// falls back to a fixed terminal speed. The cursor blinks on the same seconds the class is fed.
/// </para>
/// </remarks>
internal sealed class CommLog
{
    /// <summary>Characters per second for a line with no spoken duration.</summary>
    private const double DefaultCharactersPerSecond = 30.0;

    /// <summary>Columns the terminal wraps at.</summary>
    private const int ColumnsPerLine = 40;

    /// <summary>Rows the terminal shows; older ones have scrolled away.</summary>
    private const int VisibleLines = 6;

    /// <summary>The cursor's blink: half a second lit, half dark.</summary>
    private const double BlinkSeconds = 0.5;

    /// <summary>The terminal's palette, from the HUD's own family: green phosphor on nothing.</summary>
    private static readonly Color Ink = new(120, 225, 150);
    private static readonly Color Cursor = new(90, 235, 130);

    /// <summary>The text the terminal is still writing, with the pace of the line it came from.</summary>
    private readonly Queue<(string Row, double CharactersPerSecond)> _toType = new();

    /// <summary>The finished lines and the one being typed, oldest first.</summary>
    private readonly List<string> _rows = [];
    private string _typing = "";
    private double _typeSeconds;
    private double _charactersPerSecond = DefaultCharactersPerSecond;
    private double _blinkSeconds;

    /// <summary>Queues a line for the terminal, wrapped to the terminal's own width.</summary>
    internal void WriteLine(string line) => WriteLine(line, 0.0);

    /// <summary>
    /// The typing pace for a line, in characters per second.
    /// </summary>
    /// <remarks>
    /// The line's own length over the time the voice takes, so the text finishes when the voice
    /// does. The clamps keep a pathological duration from making the terminal crawl or sprint; a
    /// line with no voice behind it (a mission note) uses the fixed terminal speed.
    /// </remarks>
    internal static double RateFor(string line, double speakSeconds) =>
        speakSeconds > 0.05
            ? Math.Clamp(line.Length / speakSeconds, 4.0, 90.0)
            : DefaultCharactersPerSecond;

    /// <summary>
    /// Queues a line, typed at the pace the voice will speak it.
    /// </summary>
    /// <param name="line">The words.</param>
    /// <param name="speakSeconds">How long the voice takes, or zero for a note with no voice.</param>
    internal void WriteLine(string line, double speakSeconds)
    {
        List<string> rows = Wrap(line, ColumnsPerLine);

        // The rate belongs to the whole line, not to a row: a wrapped line has to keep typing at
        // the same pace across the break, and the break is an artefact of the block's width.
        double rate = RateFor(line, speakSeconds);

        foreach (string row in rows)
        {
            _toType.Enqueue((row, rate));
        }
    }

    /// <summary>Advances the typewriter and the blink by the seconds fed.</summary>
    internal void Update(double seconds)
    {
        _blinkSeconds += seconds;

        // A finished row joins the scrollback when the next one begins typing; the typing
        // of the last row keeps its place as the current row until then.
        if (_typing.Length == _typeProgress && _toType.Count > 0)
        {
            if (_typing.Length > 0)
            {
                _rows.Add(_typing);
                if (_rows.Count > 50)
                {
                    _rows.RemoveAt(0);
                }
            }

            (string row, double rate) = _toType.Dequeue();
            _typing = row;
            _charactersPerSecond = rate;
            _typeSeconds = 0.0;
        }

        if (_typing.Length > _typeProgress)
        {
            _typeSeconds += seconds;
        }
    }

    private int _typeProgress => Math.Min(_typing.Length,
        (int)Math.Floor(_typeSeconds * _charactersPerSecond));

    /// <summary>
    /// Draws the terminal: the last visible lines' text, the cursor after the last typed
    /// character, blinking.
    /// </summary>
    internal void Draw(SpriteBatch sprites, Texture2D pixel, SpriteFont font, Rectangle area)
    {
        // The terminal owns its batch scope: the panels around it each do the same, and a
        // batch left open across Draw calls is the mistake this start-up caught immediately.
        sprites.Begin();

        // The rows shown: finished ones above, the one being typed last, the block scrolled
        // so the typing is always on screen.
        string current = _typing.Length > 0 ? _typing[.._typeProgress] : "";
        List<string> shown = [.. _rows, current];
        if (shown.Count > VisibleLines)
        {
            shown = shown[^VisibleLines..];
        }

        float lineHeight = FlightUi.Line;
        Vector2 at = new(area.X + 8, area.Bottom - (shown.Count * lineHeight) - 8);

        for (int index = 0; index < shown.Count; index++)
        {
            sprites.DrawString(font, shown[index], at, Ink);
            at.Y += lineHeight;
        }

        // The cursor: a solid block after the last typed character, blinking when the
        // typewriter is between rows; solid while a row is typing.
        bool lit = _typing.Length > _typeProgress || (_blinkSeconds % (BlinkSeconds * 2)) < BlinkSeconds;
        if (lit)
        {
            string rowText = shown.Count > 0 ? shown[^1] : "";
            Vector2 advance = font.MeasureString(rowText);
            var cursorBlock = new Rectangle(
                (int)(at.X + advance.X - 4), (int)(at.Y - lineHeight + 3), 9, 14);
            sprites.Draw(pixel, cursorBlock, Cursor);
        }

        sprites.End();
    }

    /// <summary>Wraps text to whole words on the terminal's column width.</summary>
    private static List<string> Wrap(string text, int columns)
    {
        var rows = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length == 0)
            {
                current.Append(word);
                continue;
            }

            if (current.Length + 1 + word.Length <= columns)
            {
                current.Append(' ').Append(word);
                continue;
            }

            rows.Add(current.ToString());
            current.Clear();
            current.Append(word);
        }

        if (current.Length > 0)
        {
            rows.Add(current.ToString());
        }

        return rows;
    }
}
