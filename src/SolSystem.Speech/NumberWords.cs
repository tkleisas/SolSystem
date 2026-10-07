using System.Text;

namespace SolSystem.Speech;

/// <summary>
/// Spells integers and decimals the way a comm channel would say them.
/// </summary>
/// <remarks>
/// <para>
/// The docking callouts compute their numbers from the simulation, so the words have to be
/// generated, not authored: <c>0.15</c> becomes "zero point one five" — digits after the
/// point, one by one, which is how a docking controller actually speaks a rate. Integers go
/// to nine hundred and ninety-nine million, which is well past anything a range readout
/// needs to say.
/// </para>
/// <para>
/// Everything is lowercase, no punctuation, no "and": "one hundred twenty-three". The
/// normaliser would leave such a line alone either way; the words are for the model.
/// </para>
/// </remarks>
internal static class NumberWords
{
    private static readonly string[] Small =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
        "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen",
        "seventeen", "eighteen", "nineteen",
    ];

    private static readonly string[] Tens =
    [
        "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
    ];

    internal static string Say(int value)
    {
        if (value < 0)
        {
            return "minus " + Say(-value);
        }

        if (value < 20)
        {
            return Small[value];
        }

        if (value < 100)
        {
            int tens = value / 10;
            int ones = value % 10;
            return ones == 0 ? Tens[tens] : $"{Tens[tens]} {Small[ones]}";
        }

        if (value < 1_000)
        {
            return Hundred(value);
        }

        if (value < 1_000_000)
        {
            return $"{Hundred(value / 1_000)} thousand{Remainder(value, 1_000)}";
        }

        return $"{Hundred(value / 1_000_000)} million{Remainder(value, 1_000_000)}";
    }

    private static string Hundred(int value)
    {
        int hundreds = value / 100;
        int rest = value % 100;
        if (hundreds == 0)
        {
            return Say(rest);
        }

        string head = $"{Small[hundreds]} hundred";
        return rest == 0 ? head : $"{head} {Say(rest)}";
    }

    private static string Remainder(int value, int scale)
    {
        int rest = value % scale;
        return rest == 0 ? "" : " " + Say(rest);
    }

    /// <summary>Speaks a non-negative magnitude with its unit: "two hundred metres".</summary>
    internal static string Metres(double value) => $"{Say((int)Math.Round(value))} metres";

    /// <summary>Speaks a signed rate to two decimals: "closing zero point one five".</summary>
    internal static string Rate(double value)
    {
        bool negative = value < 0;
        double magnitude = Math.Abs(value);
        int whole = (int)magnitude;
        int hundredths = (int)Math.Round((magnitude - whole) * 100.0);
        if (hundredths >= 100)
        {
            whole += 1;
            hundredths -= 100;
        }

        var words = new StringBuilder();
        words.Append(Say(whole));
        if (hundredths > 0)
        {
            words.Append(" point");
            if (hundredths < 10)
            {
                words.Append(" zero");
            }

            foreach (char digit in hundredths.ToString().TrimEnd('0'))
            {
                words.Append(' ').Append(Small[digit - '0']);
            }
        }

        if (hundredths == 0 && whole == 0)
        {
            words.Append(" zero");
        }

        return (negative ? "minus " : "") + words.ToString();
    }
}
