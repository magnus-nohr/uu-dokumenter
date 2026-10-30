using System.Text.RegularExpressions;

namespace UuDok.Core;

/// <summary>
/// Enkel stoppordbasert språkgjenkjenning for bokmål, nynorsk og engelsk. Brukes bare til tydelige
/// tilfeller (nok ord og klar margin), aldri som eneste grunnlag for en usikker endring.
/// </summary>
public static partial class LanguageGuess
{
    private static readonly HashSet<string> Nb = ["og", "i", "det", "er", "som", "på", "en", "et", "til", "av", "for", "med", "har", "ikke", "jeg", "de", "den", "å", "vi", "kan", "skal", "om", "fra", "hva", "hvordan", "også", "eller", "men", "ved", "når", "hvis", "seg", "sin", "dette", "elevene", "må", "blir", "være", "noe", "hvor", "mye", "bare", "etter"];
    private static readonly HashSet<string> Nn = ["og", "i", "det", "er", "som", "på", "ein", "eit", "til", "av", "for", "med", "har", "ikkje", "eg", "dei", "den", "å", "vi", "kan", "skal", "om", "frå", "kva", "korleis", "òg", "eller", "men", "ved", "når", "viss", "seg", "sin", "dette", "elevane", "må", "blir", "vere", "noko", "kvar", "mykje", "berre", "etter"];
    private static readonly HashSet<string> En = ["the", "and", "of", "to", "is", "in", "that", "for", "with", "are", "this", "on", "as", "be", "it", "not", "you", "can", "or", "what", "how", "from", "by", "at", "have", "we", "they", "will", "an", "a", "students", "which", "their"];

    [GeneratedRegex(@"[\p{L}]+")] private static partial Regex Words();

    public sealed record Guess(string? Language, double Confidence, int WordCount);

    public static Guess Detect(string text)
    {
        var words = Words().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToList();
        if (words.Count < 12) return new Guess(null, 0, words.Count);
        double nb = words.Count(Nb.Contains), nn = words.Count(Nn.Contains), en = words.Count(En.Contains);
        // Særord som skiller nynorsk fra bokmål veier tyngre.
        nn += words.Count(w => w is "ikkje" or "eg" or "kva" or "korleis" or "frå" or "berre" or "noko" or "mykje" or "eit" or "dei") * 2;
        nb += words.Count(w => w is "ikke" or "jeg" or "hva" or "hvordan" or "fra" or "bare" or "noe" or "mye" or "et" or "de") * 2;
        var scores = new[] { ("nb-NO", nb), ("nn-NO", nn), ("en-GB", en) }.OrderByDescending(s => s.Item2).ToArray();
        var top = scores[0]; var second = scores[1];
        if (top.Item2 < 3) return new Guess(null, 0, words.Count);
        var conf = (top.Item2 - second.Item2) / Math.Max(1, top.Item2);
        // Bokmål vs nynorsk med lite tekst er usikkert.
        return new Guess(top.Item1, Math.Round(conf, 2), words.Count);
    }

    public static bool SameFamily(string? a, string? b)
    {
        if (a == null || b == null) return false;
        static string F(string l) => l.ToLowerInvariant() switch
        {
            var x when x.StartsWith("nb") || x.StartsWith("no") => "nb",
            var x when x.StartsWith("nn") => "nn",
            var x => x.Split('-')[0]
        };
        return F(a) == F(b);
    }
}
