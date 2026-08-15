using System;
using System.Linq;

namespace EastFive.Azure.Search
{
    /// <summary>
    /// Composes the <c>search=</c> expression for a FULL-LUCENE query from raw user input.
    /// Every search surface built on this framework goes through here, so the semantics are
    /// the same everywhere.
    ///
    /// The naive expression this replaced was <c>$"{userInput}~"</c>, which was fuzzy in the
    /// Azure sense (Damerau-Levenshtein ≤ 2 against whole index tokens) and useless in the
    /// human sense. Four things were wrong with it, and the shape below is what fixes each:
    ///
    /// 1. Edit distance is NOT prefix matching. A term can only be within 2 edits of a
    ///    2-character query if the term is itself ≤ 4 characters, so searching "ir" over a
    ///    roster containing "Irina" and "Walter J" matched the middle initial and NOT Irina.
    ///    Generally: you had to type at least len(name)-2 characters before a name could
    ///    match at all. Hence the <c>token*</c> clause — prefix matching is what a search box
    ///    is expected to do, and full Lucene syntax supports it.
    /// 2. <c>~</c> binds to ONE term, so in "Jon Wingstrom" only "Wingstrom" was fuzzy.
    ///    Hence per-token composition.
    /// 3. Adjacent terms were OR'd (searchMode defaults to `any`), so "John Smith" matched
    ///    every John plus every Smith and reported an inflated total. Hence the explicit
    ///    <c>AND</c> between token clauses — which also makes searchMode irrelevant, since
    ///    it only supplies the default conjunction for terms that carry no operator.
    /// 4. User text was interpolated into a Lucene expression unescaped, so a hyphen, paren
    ///    or slash could produce a malformed query and a 400 from the service. That is not
    ///    handled here by escaping but by <see cref="Tokenize"/>: every Lucene operator
    ///    character is a token SEPARATOR, so no operator can reach the composed expression.
    ///
    /// Splitting on non-word characters also mirrors what the standard analyzer did to the
    /// index — it breaks "Smith-Jones" into two tokens, so a query that kept the hyphen
    /// could never match. The apostrophe is the exception the analyzer also makes (UAX-29
    /// treats it as mid-letter), so "O'Brien" stays whole on both sides.
    /// </summary>
    public static class SearchText
    {
        /// <summary>The match-all expression: no usable search text.</summary>
        public const string MatchAll = "*";

        /// <summary>
        /// Below this length a token gets prefix matching only. A one-edit allowance on a
        /// short token is a huge relative distortion — "a~1" matches every single-character
        /// token in the index — and the prefix clause already covers what a short query means.
        /// </summary>
        public const int MinimumFuzzyTokenLength = 4;

        /// <summary>
        /// Splits on everything that is not a letter, digit or apostrophe, and lowercases.
        /// Lowercasing is REQUIRED, not cosmetic: wildcard and fuzzy terms bypass lexical
        /// analysis and are matched against the literal (analyzer-lowercased) index tokens,
        /// so an uppercase query term would simply fail to match.
        /// </summary>
        public static string[] Tokenize(string searchText)
            => new string((searchText ?? string.Empty)
                    .Select(character => IsTokenCharacter(character)
                        ? char.ToLowerInvariant(character)
                        : ' ')
                    .ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                // An apostrophe-only run is not a term anyone searched for.
                .Where(token => token.Any(char.IsLetterOrDigit))
                .ToArray();

        private static bool IsTokenCharacter(char character)
            => char.IsLetterOrDigit(character) || character == '\'';

        /// <summary>
        /// One token's clause: prefix match, widened by a one-edit fuzzy match once the token
        /// is long enough for a typo to be the likelier explanation than a truncation.
        /// </summary>
        private static string ClauseFor(string token)
            => token.Length < MinimumFuzzyTokenLength
                ? $"{token}*"
                : $"({token}* OR {token}~1)";

        /// <summary>
        /// The composed expression, or <see cref="MatchAll"/> when the input holds no
        /// searchable token (blank, or punctuation only).
        /// </summary>
        public static string Compose(string searchText)
        {
            var clauses = Tokenize(searchText)
                .Select(ClauseFor)
                .ToArray();
            return clauses.Any()
                ? string.Join(" AND ", clauses)
                : MatchAll;
        }

        /// <summary>True when <paramref name="composed"/> carries actual search terms —
        /// i.e. relevance ordering is meaningful.</summary>
        public static bool IsMatchAll(string composed) => composed == MatchAll;
    }
}
