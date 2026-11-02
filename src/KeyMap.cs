using System.Collections.Generic;
using System.Text;

namespace Shaqlouba
{
    internal enum Direction { None, EnToAr, ArToEn }

    // Rewrites text typed on the wrong layout, between US QWERTY and Arabic (101).
    internal static class KeyMap
    {
        // { what a key types on the English layout, what the same key types on the Arabic layout }
        // Keys that type the same thing on both layouts (digits, "!@#$%^&*", "-=_+", ":\"\\|") are left out.
        // tests/KeyMapTests.cs checks this table against the layouts installed in Windows.
        static readonly string[,] Keys =
        {
            { "`", "ذ" }, { "~", "ّ" }, { "(", ")" }, { ")", "(" },

            { "q", "ض" }, { "w", "ص" }, { "e", "ث" }, { "r", "ق" }, { "t", "ف" },
            { "y", "غ" }, { "u", "ع" }, { "i", "ه" }, { "o", "خ" }, { "p", "ح" },
            { "[", "ج" }, { "]", "د" },

            { "Q", "َ" }, { "W", "ً" }, { "E", "ُ" }, { "R", "ٌ" }, { "T", "لإ" },
            { "Y", "إ" }, { "U", "‘" }, { "I", "÷" }, { "O", "×" }, { "P", "؛" },
            { "{", "<" }, { "}", ">" },

            { "a", "ش" }, { "s", "س" }, { "d", "ي" }, { "f", "ب" }, { "g", "ل" },
            { "h", "ا" }, { "j", "ت" }, { "k", "ن" }, { "l", "م" }, { ";", "ك" }, { "'", "ط" },

            { "A", "ِ" }, { "S", "ٍ" }, { "D", "]" }, { "F", "[" }, { "G", "لأ" },
            { "H", "أ" }, { "J", "ـ" }, { "K", "،" }, { "L", "/" },

            { "z", "ئ" }, { "x", "ء" }, { "c", "ؤ" }, { "v", "ر" }, { "b", "لا" },
            { "n", "ى" }, { "m", "ة" }, { ",", "و" }, { ".", "ز" }, { "/", "ظ" },

            { "Z", "~" }, { "X", "ْ" }, { "C", "}" }, { "V", "{" }, { "B", "لآ" },
            { "N", "آ" }, { "M", "’" }, { "<", "," }, { ">", "." }, { "?", "؟" },
        };

        static readonly Dictionary<char, string> ToArabicMap = new Dictionary<char, string>();
        static readonly Dictionary<char, char> ToEnglishMap = new Dictionary<char, char>();
        static readonly Dictionary<string, char> ToEnglishPairs = new Dictionary<string, char>();

        // Words where the two keys g+h (typed as "لا") end a word, instead of the single key b.
        static readonly HashSet<string> GhEndings = new HashSet<string>
        {
            "high", "thigh", "sigh", "weigh", "neigh", "though", "although", "through", "thorough",
            "enough", "rough", "tough", "cough", "dough", "laugh",
        };

        static KeyMap()
        {
            for (int i = 0; i < Keys.GetLength(0); i++)
            {
                char en = Keys[i, 0][0];
                string ar = Keys[i, 1];
                ToArabicMap[en] = ar;
                if (ar.Length == 1) ToEnglishMap[ar[0]] = en;
                else ToEnglishPairs[ar] = en;
            }

            // Word turns a typed ' into a curly quote before we ever see it.
            ToArabicMap['‘'] = "ط";
            ToArabicMap['’'] = "ط";
        }

        // Flips the whole text in the direction its letters suggest.
        public static string Flip(string text, out Direction direction)
        {
            direction = Detect(text);
            return Convert(text, direction);
        }

        // Flips only the last run of same-script words, so "مرحبا hgsghl" keeps its correct start.
        public static string FlipLastRun(string line, out Direction direction)
        {
            direction = Direction.None;
            int runStart = -1;
            int i = line.Length;
            while (i > 0)
            {
                while (i > 0 && char.IsWhiteSpace(line[i - 1])) i--;
                int end = i;
                while (i > 0 && !char.IsWhiteSpace(line[i - 1])) i--;
                if (end == i) break;

                Direction word = ScriptOf(line, i, end);
                if (word == Direction.None) continue;
                if (direction == Direction.None) direction = word;
                else if (word != direction) break;
                runStart = i;
            }

            if (direction == Direction.None) return Flip(line, out direction);
            return line.Substring(0, runStart) + Convert(line.Substring(runStart), direction);
        }

        public static Direction Detect(string text)
        {
            bool mappable = false;
            foreach (char c in text)
                if (ToArabicMap.ContainsKey(c)) mappable = true;

            Direction script = ScriptOf(text, 0, text.Length);
            if (script != Direction.None) return script;
            return mappable ? Direction.EnToAr : Direction.None;
        }

        public static string Convert(string text, Direction direction)
        {
            if (direction == Direction.EnToAr) return ToArabic(text);
            if (direction == Direction.ArToEn) return ToEnglish(text);
            return text;
        }

        static string ToArabic(string s)
        {
            var result = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = IsAutoCapitalized(s, i) ? char.ToLowerInvariant(s[i]) : s[i];
                string ar;
                if (ToArabicMap.TryGetValue(c, out ar)) result.Append(ar);
                else result.Append(c);
            }
            return result.ToString();
        }

        static string ToEnglish(string s)
        {
            var result = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == 'ل' && i + 1 < s.Length)
                {
                    if (s[i + 1] == 'ا')
                    {
                        result.Append(LooksLikeGh(result, s, i + 2) ? "gh" : "b");
                        i++;
                        continue;
                    }

                    char pair;
                    if (ToEnglishPairs.TryGetValue(s.Substring(i, 2), out pair))
                    {
                        result.Append(pair);
                        i++;
                        continue;
                    }
                }

                char en;
                result.Append(ToEnglishMap.TryGetValue(s[i], out en) ? en : s[i]);
            }
            return result.ToString();
        }

        // Office capitalizes the first letter of a sentence, so "hgsghl" arrives as "Hgsghl".
        // Most capitals type a mark or a symbol on the Arabic layout, which no word starts with.
        // The ones that type a real letter are only undone where the lowercase reading is far likelier.
        static bool IsAutoCapitalized(string s, int i)
        {
            char c = s[i];
            if (c < 'A' || c > 'Z') return false;
            if (i > 0 && !char.IsWhiteSpace(s[i - 1])) return false;
            if (i + 1 >= s.Length) return false;

            char next = s[i + 1];
            string nextAr;
            bool nextIsLetter = ToArabicMap.TryGetValue(next, out nextAr)
                && nextAr[0] >= 'ء' && nextAr[0] <= 'ي'
                && !(next >= 'A' && next <= 'Z');
            if (!nextIsLetter) return false;

            if (c == 'Y' || c == 'N') return false;  // إ and آ
            if (c == 'H') return next == 'g';        // "ال", but keep "أنا"
            if (c == 'G') return next != 'k';        // keep "لأن"
            return true;
        }

        // The Arabic layout types "لا" for the b key, and g then h type the same two characters.
        // b is the far more common reading, except in the "-ight" and "-ough" family.
        static bool LooksLikeGh(StringBuilder done, string s, int next)
        {
            int wordStart = done.Length;
            while (wordStart > 0 && char.IsLetter(done[wordStart - 1])) wordStart--;
            string before = done.ToString(wordStart, done.Length - wordStart).ToLowerInvariant();
            if (before == "hi") return true;  // high, higher, highway

            char after = ' ';
            if (next < s.Length && !ToEnglishMap.TryGetValue(s[next], out after)) after = s[next];

            if (after == 't')
            {
                if (before.EndsWith("dou")) return false;  // doubt
                return before.EndsWith("i") || before.EndsWith("ou") || before.EndsWith("au");
            }
            return !char.IsLetter(after) && GhEndings.Contains(before + "gh");
        }

        static Direction ScriptOf(string text, int start, int end)
        {
            int latin = 0, arabic = 0;
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
                else if (IsArabic(c)) arabic++;
            }
            if (latin == 0 && arabic == 0) return Direction.None;
            return arabic > latin ? Direction.ArToEn : Direction.EnToAr;
        }

        static bool IsArabic(char c)
        {
            bool digit = (c >= '٠' && c <= '٩') || (c >= '۰' && c <= '۹');
            return c >= '؀' && c <= 'ۿ' && !digit;
        }
    }
}
