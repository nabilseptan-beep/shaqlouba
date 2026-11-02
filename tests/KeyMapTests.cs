using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Shaqlouba
{
    // Console test runner: no framework, so the in-box compiler can build it.
    internal static class KeyMapTests
    {
        static int failures;

        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            FlipCases();
            LastRunCases();
            LayoutTableMatchesWindows();
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

            // g+h types the same two characters, so common "gh" words are recognised.
            foreach (string word in new[] { "right", "light", "thought", "caught", "high", "higher", "through", "enough", "eight", "highlight" })
                ExpectFlip(word, word, Direction.ArToEn, true);
            foreach (string word in new[] { "club", "doubt", "github", "web", "lib", "about", "subject" })
                ExpectFlip(word, word, Direction.ArToEn, true);

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
        }

        // Asks Windows what every key types on each installed layout and compares it with our table.
        static void LayoutTableMatchesWindows()
        {
            IntPtr en = FindLayout(0x09), ar = FindLayout(0x01);
            if (en == IntPtr.Zero || ar == IntPtr.Zero)
            {
                Console.WriteLine("SKIP layout check: English and Arabic layouts are not both installed");
                return;
            }

            int compared = 0;
            for (uint vk = 0x20; vk < 0xE0; vk++)
            {
                // The numpad types the same symbols on both layouts, so its "." and "/" can't be told apart from the main keys.
                if (vk >= 0x60 && vk <= 0x6F) continue;

                foreach (bool shift in new[] { false, true })
                {
                    string typedEn = Typed(vk, shift, en), typedAr = Typed(vk, shift, ar);
                    if (typedEn == null || typedAr == null || typedEn.Length != 1) continue;
                    compared++;

                    string mapped = KeyMap.Convert(typedEn, Direction.EnToAr);
                    if (mapped != typedAr)
                    {
                        Fail(string.Format("key 0x{0:X2} shift={1}: Windows types [{2}] -> [{3}], table gives [{4}]", vk, shift, typedEn, typedAr, mapped));
                    }

                    if (typedEn == " " || typedAr == typedEn) continue;
                    string back = KeyMap.Convert(typedAr, Direction.ArToEn);
                    if (back != typedEn)
                    {
                        Fail(string.Format("key 0x{0:X2} shift={1}: [{2}] should map back to [{3}], table gives [{4}]", vk, shift, typedAr, typedEn, back));
                    }
                }
            }
            Console.WriteLine("layout check: compared " + compared + " key positions");
        }

        static string Typed(uint vk, bool shift, IntPtr layout)
        {
            var state = new byte[256];
            if (shift) state[0x10] = 0x80;
            var buffer = new StringBuilder(8);
            uint scan = MapVirtualKeyEx(vk, 0, layout);
            int count = ToUnicodeEx(vk, scan, state, buffer, buffer.Capacity, 4, layout);
            if (count <= 0) return null;
            string typed = buffer.ToString(0, count);
            return typed[0] < ' ' ? null : typed;
        }

        static IntPtr FindLayout(int primaryLanguage)
        {
            var list = new IntPtr[32];
            int count = GetKeyboardLayoutList(list.Length, list);
            for (int i = 0; i < count; i++)
                if ((list[i].ToInt64() & 0x3FF) == primaryLanguage) return list[i];
            return IntPtr.Zero;
        }

        static void ExpectFlip(string input, string expected, Direction direction, bool viaArabic = false)
        {
            if (viaArabic) input = KeyMap.Convert(input, Direction.EnToAr);
            Direction actualDirection;
            string actual = KeyMap.Flip(input, out actualDirection);
            if (actual != expected || actualDirection != direction)
                Fail(string.Format("Flip([{0}]) = [{1}] ({2}), expected [{3}] ({4})", input, actual, actualDirection, expected, direction));
        }

        static void ExpectLastRun(string input, string expected, Direction direction)
        {
            Direction actualDirection;
            string actual = KeyMap.FlipLastRun(input, out actualDirection);
            if (actual != expected || actualDirection != direction)
                Fail(string.Format("FlipLastRun([{0}]) = [{1}] ({2}), expected [{3}] ({4})", input, actual, actualDirection, expected, direction));
        }

        static void Fail(string message)
        {
            failures++;
            Console.WriteLine("FAIL " + message);
        }

        [DllImport("user32.dll")]
        static extern int GetKeyboardLayoutList(int count, [Out] IntPtr[] list);

        [DllImport("user32.dll")]
        static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr layout);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder buffer, int size, uint flags, IntPtr layout);
    }
}
