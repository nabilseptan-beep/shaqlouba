using System;
using System.Text;

namespace Shaqlouba
{
    // Console test runner: no framework, so the in-box compiler can build it.
    // The cases assume the English (US) and Arabic (101) layouts, so both must be installed.
    internal static class KeyMapTests
    {
        static int failures;
        static KeyMap map;

        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            IntPtr en = FindLayout(0x0409), ar = FindLayout(0x0401);
            if (en == IntPtr.Zero || ar == IntPtr.Zero)
            {
                Console.WriteLine("English (US) and Arabic (101) must both be installed in Windows to run the tests.");
                return 1;
            }
            map = new KeyMap(en, ar);

            FlipCases();
            LastRunCases();
            ArabicWordsAreNotEnglish();
            LayoutPicking(en, ar);
            TableMatchesWindows(en, ar);
            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            return failures == 0 ? 0 : 1;
        }

        static void FlipCases()
        {
            ExpectFlip("hgsghl ugd;l", "السلام عليكم", Direction.EnToAr);
            ExpectFlip("اثممخ صخقمي", "hello world", Direction.ArToEn);
            ExpectFlip("sghl", "سلام", Direction.EnToAr);
            ExpectFlip("123", "123", Direction.None);
            ExpectFlip("", "", Direction.None);
            ExpectFlip(";", "ك", Direction.EnToAr);
            ExpectFlip("line one\r\nline two", "مهىث خىث\r\nمهىث فصخ", Direction.EnToAr);

            // The b key types two characters on the Arabic layout.
            ExpectFlip("table", "فشلامث", Direction.EnToAr);
            ExpectFlip("فشلامث", "table", Direction.ArToEn);

            // g+h types the same two characters, so the reading the dictionary knows wins.
            foreach (string word in new[] { "right", "light", "thought", "caught", "high", "higher", "through", "enough", "eight", "highlight", "ghost", "ghastly", "ghost,", "thoughtfully" })
                ExpectFlip(word, word, Direction.ArToEn, true);
            ExpectFlip("ٌهلاف", "Right", Direction.ArToEn);
            foreach (string word in new[] { "club", "doubt", "github", "web", "lib", "about", "subject", "doubtful" })
                ExpectFlip(word, word, Direction.ArToEn, true);

            // Without a dictionary word, the one-key reading (b) stands.
            ExpectFlip("bxq", "bxq", Direction.ArToEn, true);

            // Office auto-capitalizes the first letter.
            ExpectFlip("Hgsghl ugd;l", "السلام عليكم", Direction.EnToAr);
            ExpectFlip("Sghl", "سلام", Direction.EnToAr);
            ExpectFlip("Td", "في", Direction.EnToAr);
            ExpectFlip("Hkh", "أنا", Direction.EnToAr);
            ExpectFlip("Gk", "لأن", Direction.EnToAr);
            ExpectFlip("Ydv", "إير", Direction.EnToAr);
            ExpectFlip("‘vdr", "طريق", Direction.EnToAr);
        }

        static void LastRunCases()
        {
            ExpectLastRun("مرحبا hgsghl", "مرحبا السلام", Direction.EnToAr);
            ExpectLastRun("hello مرحبا", "hello lvpfh", Direction.ArToEn);
            ExpectLastRun("hgsghl ugd;l", "السلام عليكم", Direction.EnToAr);
            ExpectLastRun("كلام صح 123 hgsghl ugd;l", "كلام صح 123 السلام عليكم", Direction.EnToAr);
            ExpectLastRun("كلام hgsghl 123", "كلام السلام 123", Direction.EnToAr);
            ExpectLastRun("  ", "  ", Direction.None);

            // Real English words in front of the wrong part stay.
            ExpectLastRun("Hello hgsghl", "Hello السلام", Direction.EnToAr);
            ExpectLastRun("see you, hgsghl ugd;l", "see you, السلام عليكم", Direction.EnToAr);

            // The last word is flipped even when it is a word, and two-letter words don't count.
            ExpectLastRun("hello", "اثممخ", Direction.EnToAr);
            ExpectLastRun("ok hgsghl", "خن السلام", Direction.EnToAr);
            ExpectLastRun("hi hdi", "اه ايه", Direction.EnToAr);
        }

        // Common Arabic words typed on the English layout must not pass for English, or the run would stop at them.
        static void ArabicWordsAreNotEnglish()
        {
            string words = "اه هي مش انا ايه كده علي بس لما ازي عشان طيب تمام ليه فين مين ماشي خلاص جدا كتير شوية كل يوم ربنا الله "
                + "حبيبي عايز نفس وقت اول اخر بيت شغل ناس عين ولا كمان دلوقتي يعني لازم بقى ممكن طب برضه اصلا حاجة اكيد جامد "
                + "حلو وحش سلام اهلا معاك عليك منك ليك تحت فوق جوه بره هنا هناك امتى دكتور فعلا عبد ماما بابا عمر اسم رقم عيد "
                + "تاني تالت نص ربع حد كلب قلب عشر صبر ابو بنت ولد راجل ست صح غلط نعم ازاي كويس تعبان مبسوط حضرتك اللي عارف "
                + "عاوز بتاع شكرا مرحبا يلا ايوه لسه خالص اوي روح تعال هات امشي اقعد اتصل بعت وصل جاب راح عمل قال شاف سمع عرف "
                + "فهم حب زعل فرح ضحك نام اكل شرب لعب كتب درس فتح قفل بص طلع نزل دخل خرج رجع فضل بدأ خلص غاب حضر";
            foreach (string word in words.Split(' '))
            {
                string typed = map.Convert(word, Direction.ArToEn);
                if (Words.Contains(typed))
                    Fail(string.Format("[{0}] typed on the English layout is [{1}], which the dictionary takes for English", word, typed));
            }
        }

        // The window's own layout is used for its script, and the first installed one for the other.
        static void LayoutPicking(IntPtr en, IntPtr ar)
        {
            foreach (IntPtr current in new[] { en, ar, IntPtr.Zero })
            {
                KeyMap picked = KeyMap.For(current);
                if (picked == null)
                {
                    Fail("KeyMap.For(" + current + ") found no layouts");
                    continue;
                }
                if (current == en && picked.LatinLayout != en) Fail("KeyMap.For(en) did not keep the English layout");
                if (current == ar && picked.ArabicLayout != ar) Fail("KeyMap.For(ar) did not keep the Arabic layout");
                if (picked.LatinLayout == picked.ArabicLayout) Fail("KeyMap.For picked the same layout for both scripts");
            }
        }

        // Every key must flip to what Windows types on the other layout, and back.
        static void TableMatchesWindows(IntPtr en, IntPtr ar)
        {
            int compared = 0;
            for (uint vk = 0x20; vk < 0xE0; vk++)
            {
                if (vk >= 0x60 && vk <= 0x6F) continue;

                foreach (bool shift in new[] { false, true })
                {
                    string typedEn = KeyMap.Typed(vk, shift, en), typedAr = KeyMap.Typed(vk, shift, ar);
                    if (typedEn == null || typedAr == null || typedEn.Length != 1) continue;
                    compared++;

                    string mapped = map.Convert(typedEn, Direction.EnToAr);
                    if (mapped != typedAr)
                        Fail(string.Format("key 0x{0:X2} shift={1}: Windows types [{2}] -> [{3}], table gives [{4}]", vk, shift, typedEn, typedAr, mapped));

                    if (typedEn == " " || typedAr == typedEn) continue;
                    string back = map.Convert(typedAr, Direction.ArToEn);
                    if (back != typedEn)
                        Fail(string.Format("key 0x{0:X2} shift={1}: [{2}] should map back to [{3}], table gives [{4}]", vk, shift, typedAr, typedEn, back));
                }
            }
            Console.WriteLine("layout check: compared " + compared + " key positions");
        }

        // The layout with this id in the high word of its handle, like 0x0409 for English (US).
        static IntPtr FindLayout(long layoutId)
        {
            foreach (IntPtr layout in KeyMap.InstalledLayouts(IntPtr.Zero))
                if (((layout.ToInt64() >> 16) & 0xFFFF) == layoutId) return layout;
            return IntPtr.Zero;
        }

        static void ExpectFlip(string input, string expected, Direction direction, bool viaArabic = false)
        {
            if (viaArabic) input = map.Convert(input, Direction.EnToAr);
            Direction actualDirection;
            string actual = map.Flip(input, out actualDirection);
            if (actual != expected || actualDirection != direction)
                Fail(string.Format("Flip([{0}]) = [{1}] ({2}), expected [{3}] ({4})", input, actual, actualDirection, expected, direction));
        }

        static void ExpectLastRun(string input, string expected, Direction direction)
        {
            Direction actualDirection;
            string actual = map.FlipLastRun(input, out actualDirection);
            if (actual != expected || actualDirection != direction)
                Fail(string.Format("FlipLastRun([{0}]) = [{1}] ({2}), expected [{3}] ({4})", input, actual, actualDirection, expected, direction));
        }

        static void Fail(string message)
        {
            failures++;
            Console.WriteLine("FAIL " + message);
        }
    }
}
