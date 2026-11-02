using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Shaqlouba
{
    internal enum Direction { None, EnToAr, ArToEn }

    // Rewrites text typed on the wrong layout, between a Latin-script layout and an Arabic-script one.
    // The table is read from the layouts installed in Windows, so any pair works: US, UK or French on
    // one side, Arabic (101), (102) or AZERTY on the other.
    internal sealed class KeyMap
    {
        const byte VK_SHIFT = 0x10;
        const uint DontChangeKeyboardState = 4;
        const int MaxReadings = 64;

        public readonly IntPtr LatinLayout, ArabicLayout;

        // { what a key types on the Latin layout, what the same key types on the Arabic layout }
        // Keys that type the same thing on both layouts (digits, most symbols) are left out.
        readonly Dictionary<char, string> ToArabicMap = new Dictionary<char, string>();
        readonly Dictionary<char, char> ToEnglishMap = new Dictionary<char, char>();
        readonly Dictionary<string, char> ToEnglishPairs = new Dictionary<string, char>();

        [DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int count, [Out] IntPtr[] list);
        [DllImport("user32.dll")] static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr layout);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder buffer, int size, uint flags, IntPtr layout);

        // The layouts to flip between: the window's own layout for its script, the first installed one
        // for the other script. Null when no Latin or no Arabic layout is installed.
        public static KeyMap For(IntPtr currentLayout)
        {
            IntPtr latin = IntPtr.Zero, arabic = IntPtr.Zero;
            foreach (IntPtr layout in InstalledLayouts(currentLayout))
            {
                Direction script = ScriptOfLayout(layout);
                if (script == Direction.EnToAr && latin == IntPtr.Zero) latin = layout;
                if (script == Direction.ArToEn && arabic == IntPtr.Zero) arabic = layout;
            }
            if (latin == IntPtr.Zero || arabic == IntPtr.Zero) return null;
            return new KeyMap(latin, arabic);
        }

        public KeyMap(IntPtr latinLayout, IntPtr arabicLayout)
        {
            LatinLayout = latinLayout;
            ArabicLayout = arabicLayout;

            for (uint vk = 0x20; vk < 0xE0; vk++)
            {
                // The numpad types the same symbols on both layouts, so its "." and "/" can't be told apart from the main keys.
                if (vk >= 0x60 && vk <= 0x6F) continue;

                foreach (bool shift in new[] { false, true })
                {
                    string en = Typed(vk, shift, latinLayout), ar = Typed(vk, shift, arabicLayout);
                    if (en == null || ar == null || en.Length != 1 || en == ar) continue;
                    ToArabicMap[en[0]] = ar;
                    if (ar.Length == 1) ToEnglishMap[ar[0]] = en[0];
                    else ToEnglishPairs[ar] = en[0];
                }
            }

            // Word turns a typed ' into a curly quote before we ever see it.
            string quoteKey;
            if (ToArabicMap.TryGetValue('\'', out quoteKey))
            {
                ToArabicMap['‘'] = quoteKey;
                ToArabicMap['’'] = quoteKey;
            }
        }

        // The installed layouts, with the given one first.
        public static List<IntPtr> InstalledLayouts(IntPtr first)
        {
            var list = new IntPtr[32];
            int count = GetKeyboardLayoutList(list.Length, list);
            var layouts = new List<IntPtr>();
            if (first != IntPtr.Zero) layouts.Add(first);
            for (int i = 0; i < count; i++)
                if (list[i] != first) layouts.Add(list[i]);
            return layouts;
        }

        // What a key types on a layout: null for nothing, a control character or a dead key.
        public static string Typed(uint vk, bool shift, IntPtr layout)
        {
            var state = new byte[256];
            if (shift) state[VK_SHIFT] = 0x80;
            var buffer = new StringBuilder(8);
            uint scan = MapVirtualKeyEx(vk, 0, layout);
            int count = ToUnicodeEx(vk, scan, state, buffer, buffer.Capacity, DontChangeKeyboardState, layout);
            if (count <= 0) return null;
            string typed = buffer.ToString(0, count);
            return typed[0] < ' ' ? null : typed;
        }

        // The script the letter keys type: EnToAr for a Latin layout, ArToEn for an Arabic one.
        static Direction ScriptOfLayout(IntPtr layout)
        {
            var letters = new StringBuilder();
            for (uint vk = 'A'; vk <= 'Z'; vk++)
            {
                string typed = Typed(vk, false, layout);
                if (typed != null) letters.Append(typed);
            }
            return ScriptOf(letters.ToString(), 0, letters.Length);
        }

        // Flips the whole text in the direction its letters suggest.
        public string Flip(string text, out Direction direction)
        {
            direction = Detect(text);
            return Convert(text, direction);
        }

        // Flips only the last run of same-script words, so "مرحبا hgsghl" keeps its correct start.
        public string FlipLastRun(string line, out Direction direction)
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

                // A real English word in front of the run is the correct text, as in "Hello hgsghl".
                // The last word is flipped regardless: it's what the hotkey was pressed for.
                if (word == Direction.EnToAr && runStart >= 0 && Words.Contains(line.Substring(i, end - i))) break;
                runStart = i;
            }

            if (direction == Direction.None) return Flip(line, out direction);
            return line.Substring(0, runStart) + Convert(line.Substring(runStart), direction);
        }

        public Direction Detect(string text)
        {
            bool mappable = false;
            foreach (char c in text)
                if (ToArabicMap.ContainsKey(c)) mappable = true;

            Direction script = ScriptOf(text, 0, text.Length);
            if (script != Direction.None) return script;
            return mappable ? Direction.EnToAr : Direction.None;
        }

        public string Convert(string text, Direction direction)
        {
            if (direction == Direction.EnToAr) return ToArabic(text);
            if (direction == Direction.ArToEn) return ToEnglish(text);
            return text;
        }

        string ToArabic(string s)
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

        string ToEnglish(string s)
        {
            var result = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                int end = i;
                while (end < s.Length && !char.IsWhiteSpace(s[end])) end++;
                if (end == i)
                {
                    result.Append(s[i]);
                    i++;
                    continue;
                }
                result.Append(ToEnglishWord(s.Substring(i, end - i)));
                i = end;
            }
            return result.ToString();
        }

        // A key that types two characters, like b typing "لا", reads the same as the two keys that type
        // them one by one (g then h), so each such spot doubles the readings of a word. The first reading
        // the dictionary knows wins; failing that, the one-key reading, which is right far more often.
        string ToEnglishWord(string word)
        {
            var readings = new List<string>();
            Read(word, 0, new StringBuilder(word.Length), readings);
            foreach (string reading in readings)
                if (Words.Contains(reading)) return reading;
            return readings[0];
        }

        // Collects the readings depth first, taking the one-key reading at every spot before the other,
        // so the first reading collected is the all-one-key one.
        void Read(string word, int i, StringBuilder done, List<string> readings)
        {
            if (readings.Count == MaxReadings) return;
            if (i == word.Length)
            {
                readings.Add(done.ToString());
                return;
            }

            int mark = done.Length;
            char en;
            if (i + 1 < word.Length && ToEnglishPairs.TryGetValue(word.Substring(i, 2), out en))
            {
                done.Append(en);
                Read(word, i + 2, done, readings);
                done.Length = mark;
            }
            done.Append(ToEnglishMap.TryGetValue(word[i], out en) ? en : word[i]);
            Read(word, i + 1, done, readings);
            done.Length = mark;
        }

        // Office capitalizes the first letter of a sentence, so "hgsghl" arrives as "Hgsghl".
        // Most capitals type a mark or a symbol on the Arabic layout, which no word starts with.
        // The ones that type a real letter are only undone where the lowercase reading is far likelier.
        bool IsAutoCapitalized(string s, int i)
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
