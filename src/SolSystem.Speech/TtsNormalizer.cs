using System.Text;
using System.Text.RegularExpressions;

namespace SolSystem.Speech;

/// <summary>
/// The robust input normaliser the reference runtime applies before synthesis.
/// </summary>
/// <remarks>
/// <para>
/// A port of <c>tts_robust_normalizer_single_script.py</c>'s cleaning pass: protect
/// high-risk tokens (URLs, e-mail, mentions, hashtags, file-like spans), fold underscores
/// into spaces outside them, collapse whitespace, turn structural punctuation into
/// quotable or sentence-boundary shapes, and make sure every line ends in punctuation.
/// </para>
/// <para>
/// What is deliberately <b>not</b> here: WeTextProcessing's semantic expansion of numbers,
/// dates and units. The narrator authors its lines with numbers already spelled out — and
/// the dynamic callouts generate words directly — so the semantic pass has nothing to do.
/// The CJK-specific space rules are carried over because they cost nothing and keep the
/// behaviour identical if a line arrives in Chinese or Japanese.
/// </para>
/// </remarks>
internal static class TtsNormalizer
{
    private const string Cjk = @"[\u3400-\u4dbf\u4e00-\u9fff\u3040-\u30ff]";
    private const string Prot = @"___PROT\d+___";

    private static readonly Regex ZeroWidth = new(@"[\u200b-\u200d\ufeff]", RegexOptions.Compiled);
    private static readonly Regex Url =
        new(@"https?://[^\s\u3000，。！？；、）】》〉」』]+", RegexOptions.Compiled);
    private static readonly Regex Email =
        new(@"(?<![\w.+-])[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}(?![\w.-])", RegexOptions.Compiled);
    private static readonly Regex Mention =
        new(@"(?<![A-Za-z0-9_])@[A-Za-z0-9_]{1,32}", RegexOptions.Compiled);
    private static readonly Regex Reddit =
        new(@"(?<![A-Za-z0-9_])(?:u|r)/[A-Za-z0-9_]+", RegexOptions.Compiled);
    private static readonly Regex Hashtag =
        new(@"(?<![A-Za-z0-9_])#(?!\s)[^\s#]+", RegexOptions.Compiled);
    private static readonly Regex DotToken =
        new(@"(?<![A-Za-z0-9_])\.(?=[A-Za-z0-9._-]*[A-Za-z0-9])[A-Za-z0-9._-]+", RegexOptions.Compiled);
    private static readonly Regex Filelike =
        new(@"(?<![A-Za-z0-9_])(?=[A-Za-z0-9._/+:-]*[A-Za-z])(?=[A-Za-z0-9._/+:-]*[./+:-])[A-Za-z0-9][A-Za-z0-9._/+:-]*(?![A-Za-z0-9_])", RegexOptions.Compiled);
    private static readonly Regex Latinish =
        new($@"(?:{Prot}|(?=[A-Za-z0-9._/+:-]*[A-Za-z])[A-Za-z0-9][A-Za-z0-9._/+:-]*)", RegexOptions.Compiled);

    private static readonly Regex MarkdownLink =
        new(@"\[([^\[\]]+?)\]\((https?://[^)\s]+)\)", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^#{1,6}\s+", RegexOptions.Compiled);
    private static readonly Regex Quote = new(@"^>\s+", RegexOptions.Compiled);
    private static readonly Regex Bullet = new(@"^[-*+]\s+", RegexOptions.Compiled);
    private static readonly Regex Numbered = new(@"^\d+[.)]\s+", RegexOptions.Compiled);
    private static readonly Regex FlowArrow =
        new(@"\s*(?:<[-=]+>|[-=]+>|<[-=]+|[→←↔⇒⇐⇔⟶⟵⟷⟹⟸⟺↦↤↪↩])\s*", RegexOptions.Compiled);

    private static readonly Regex RunOfSpaces = new(@"[ \t\r\f\v]+", RegexOptions.Compiled);
    private static readonly Regex CjkInterior = new($@"({Cjk})\s+(?={Cjk})", RegexOptions.Compiled);
    private static readonly Regex CjkBeforeDigit = new($@"({Cjk})\s+(?=\d)", RegexOptions.Compiled);
    private static readonly Regex DigitBeforeCjk = new($@"(\d)\s+(?={Cjk})", RegexOptions.Compiled);
    private static readonly Regex CjkBeforeLatin = new($@"({Cjk})(?=({Latinish}))", RegexOptions.Compiled);
    private static readonly Regex LatinBeforeCjk = new($@"(({Latinish}))(?={Cjk})", RegexOptions.Compiled);
    private static readonly Regex MultiSpace = new(@" {2,}", RegexOptions.Compiled);
    private static readonly Regex SpaceBeforeCjkPunct = new(@"\s+([，。！？；：、”’」』】）》])", RegexOptions.Compiled);
    private static readonly Regex SpaceAfterCjkOpen = new(@"([（【「『《“‘])\s+", RegexOptions.Compiled);
    private static readonly Regex CjkPunctInterior = new(@"([，。！？；：、])\s*", RegexOptions.Compiled);
    private static readonly Regex SpaceBeforeAsciiPunct = new(@"\s+([,.;!?])", RegexOptions.Compiled);

    private static readonly Regex BracketSquare = new(@"\[\s*([^\[\]]+?)\s*\]", RegexOptions.Compiled);
    private static readonly Regex BracketCurly = new(@"\{\s*([^{}]+?)\s*\}", RegexOptions.Compiled);
    private static readonly Regex BracketCjk = new(@"[【〖『「]\s*([^】〗』」]+?)\s*[】〗』」]", RegexOptions.Compiled);
    private static readonly Regex StandaloneTitle =
        new(@"(^|[。！？!?；;]\s*)《([^》]+)》(?=\s*(?:___PROT\d+___|[—–―-]{2,}|$|[。！？!?；;，,]))", RegexOptions.Compiled);
    private static readonly Regex DashRun = new(@"\s*(?:—|–|―|-){2,}\s*", RegexOptions.Compiled);

    private static readonly Regex Ellipsis = new(@"(?:\.{3,}|…{2,}|……+)", RegexOptions.Compiled);
    private static readonly Regex CjkPeriodRun = new(@"[。．]{2,}", RegexOptions.Compiled);
    private static readonly Regex CommaRun = new(@"[，,]{2,}", RegexOptions.Compiled);
    private static readonly Regex BangRun = new(@"[!！]{2,}", RegexOptions.Compiled);
    private static readonly Regex QuestionRun = new(@"[?？]{2,}", RegexOptions.Compiled);
    private static readonly Regex MixedBangQuestion = new(@"[!?！？]{2,}", RegexOptions.Compiled);

    private static readonly HashSet<char> TrailingClosers = ['"', '\'', ')', ']', '}', '）', '】', '》', '〉', '」', '』', '”', '’'];

    internal static string Normalize(string text)
    {
        text = BaseCleanup(text);
        text = NormalizeMarkdownAndLines(text);
        text = NormalizeFlowArrows(text);
        List<string> protectedSpans = ProtectSpans(ref text);
        text = NormalizeVisibleUnderscores(text);

        text = NormalizeSpaces(text);
        text = NormalizeStructuralPunctuation(text);
        text = NormalizeRepeatedPunctuation(text);
        text = NormalizeSpaces(text);

        text = RestoreSpans(text, protectedSpans);
        text = text.Trim();
        return EnsureTerminalPunctuationByLine(text);
    }

    private static string BaseCleanup(string text)
    {
        text = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\u3000", " ");
        text = ZeroWidth.Replace(text, "");

        var cleaned = new StringBuilder(text.Length);
        foreach (char character in text)
        {
            if (character is '\n' or '\t' or ' ' || !char.IsControl(character))
            {
                cleaned.Append(character);
            }
        }

        return cleaned.ToString();
    }

    private static string NormalizeMarkdownAndLines(string text)
    {
        text = MarkdownLink.Replace(text, "$1 $2");

        var lines = new List<string>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            line = Heading.Replace(line, "");
            line = Quote.Replace(line, "");
            line = Bullet.Replace(line, "");
            line = Numbered.Replace(line, "");
            lines.Add(line);
        }

        if (lines.Count == 0)
        {
            return "";
        }

        var merged = new List<string> { lines[0] };
        for (int index = 1; index < lines.Count; index++)
        {
            string previous = merged[^1];
            merged[^1] = EnsureTerminalPunctuation(previous);
            merged.Add(lines[index]);
        }

        return string.Concat(merged);
    }

    private static List<string> ProtectSpans(ref string text)
    {
        var protectedSpans = new List<string>();
        string Replacement(Match match)
        {
            int index = protectedSpans.Count;
            protectedSpans.Add(match.Value);
            return $"___PROT{index}___";
        }

        foreach (Regex pattern in new[] { Url, Email, Mention, Reddit, Hashtag, DotToken, Filelike })
        {
            text = pattern.Replace(text, Replacement);
        }

        return protectedSpans;
    }

    private static string RestoreSpans(string text, List<string> protectedSpans)
    {
        for (int index = 0; index < protectedSpans.Count; index++)
        {
            text = text.Replace($"___PROT{index}___", protectedSpans[index]);
        }

        return text;
    }

    private static string NormalizeVisibleUnderscores(string text)
    {
        var parts = Regex.Split(text, $"({Prot})");
        var builder = new StringBuilder(text.Length);
        foreach (string part in parts)
        {
            builder.Append(Regex.IsMatch(part, Prot) ? part : part.Replace("_", " "));
        }

        return builder.ToString();
    }

    private static string NormalizeFlowArrows(string text) => FlowArrow.Replace(text, "，");

    private static string NormalizeSpaces(string text)
    {
        text = RunOfSpaces.Replace(text, " ");
        text = CjkInterior.Replace(text, "$1");
        text = CjkBeforeDigit.Replace(text, "$1");
        text = DigitBeforeCjk.Replace(text, "$1");
        text = CjkBeforeLatin.Replace(text, "$1 ");
        text = LatinBeforeCjk.Replace(text, "$1 ");
        text = MultiSpace.Replace(text, " ");
        text = SpaceBeforeCjkPunct.Replace(text, "$1");
        text = SpaceAfterCjkOpen.Replace(text, "$1");
        text = CjkPunctInterior.Replace(text, "$1");
        text = SpaceBeforeAsciiPunct.Replace(text, "$1");
        return MultiSpace.Replace(text, " ").Trim();
    }

    private static string NormalizeStructuralPunctuation(string text)
    {
        text = BracketSquare.Replace(text, "\"$1\"");
        text = BracketCurly.Replace(text, "\"$1\"");
        text = BracketCjk.Replace(text, "\"$1\"");
        text = StandaloneTitle.Replace(text, "$1$2");
        text = NormalizeFlowArrows(text);
        return DashRun.Replace(text, "。");
    }

    private static string NormalizeRepeatedPunctuation(string text)
    {
        text = Ellipsis.Replace(text, "。");
        text = CjkPeriodRun.Replace(text, "。");
        text = CommaRun.Replace(text, "，");
        text = BangRun.Replace(text, "！");
        text = QuestionRun.Replace(text, "？");
        return MixedBangQuestion.Replace(text, match =>
        {
            string span = match.Value;
            bool hasQuestion = span.IndexOfAny(['?', '？']) >= 0;
            bool hasBang = span.IndexOfAny(['!', '！']) >= 0;
            if (hasQuestion && hasBang)
            {
                return "？！";
            }

            return hasQuestion ? "？" : "！";
        });
    }

    private static string EnsureTerminalPunctuation(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        int index = text.Length - 1;
        while (index >= 0 && char.IsWhiteSpace(text[index]))
        {
            index--;
        }

        while (index >= 0 && TrailingClosers.Contains(text[index]))
        {
            index--;
        }

        if (index >= 0 && char.IsPunctuation(text[index]))
        {
            return text;
        }

        return text + "。";
    }

    private static string EnsureTerminalPunctuationByLine(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        string[] lines = text.Split('\n');
        var normalized = new string[lines.Length];
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            normalized[index] = line.Length > 0 ? EnsureTerminalPunctuation(line) : "";
        }

        return string.Join('\n', normalized).Trim();
    }
}
