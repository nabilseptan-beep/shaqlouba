using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Shaqlouba
{
    // Common English words, for telling real words from text typed on the wrong layout.
    // The list is assets/words.txt, made by tools/make_words.py and embedded by build.bat.
    internal static class Words
    {
        static readonly HashSet<string> Set = Load();

        // True for a word of the list, ignoring its case and any punctuation around it.
        public static bool Contains(string word)
        {
            int start = 0, end = word.Length;
            while (start < end && !char.IsLetter(word[start])) start++;
            while (end > start && !char.IsLetter(word[end - 1])) end--;
            if (start == end) return false;
            return Set.Contains(word.Substring(start, end - start).ToLowerInvariant().Replace('’', '\''));
        }

        static HashSet<string> Load()
        {
            var words = new HashSet<string>();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shaqlouba.words.txt"))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    if (!line.StartsWith("#")) words.Add(line);
            }
            return words;
        }
    }
}
