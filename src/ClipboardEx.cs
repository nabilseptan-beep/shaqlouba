using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Shaqlouba
{
    // A byte-for-byte copy of everything on the clipboard: text, images, files, private formats.
    internal sealed class ClipboardSnapshot
    {
        public readonly List<KeyValuePair<uint, byte[]>> Formats = new List<KeyValuePair<uint, byte[]>>();
        public IntPtr EnhancedMetafile;
    }

    // Win32 clipboard access. Formats we write are hidden from clipboard history (Win+V),
    // so flipping doesn't fill it with temporary entries.
    internal static class ClipboardEx
    {
        const long MaxFormatBytes = 256L * 1024 * 1024;

        static bool Open(IntPtr owner)
        {
            // Other programs hold the clipboard open for a moment, so retry briefly.
            for (int i = 0; i < 25; i++)
            {
                if (Native.OpenClipboard(owner)) return true;
                Thread.Sleep(10);
            }
            return false;
        }

        // Returns null if the clipboard couldn't be opened.
        public static ClipboardSnapshot Take(IntPtr owner)
        {
            if (!Open(owner)) return null;
            try
            {
                var snapshot = new ClipboardSnapshot();
                uint format = 0;
                while ((format = Native.EnumClipboardFormats(format)) != 0)
                {
                    IntPtr data = Native.GetClipboardData(format);
                    if (data == IntPtr.Zero) continue;

                    if (format == Native.CF_ENHMETAFILE)
                    {
                        snapshot.EnhancedMetafile = Native.CopyEnhMetaFile(data, IntPtr.Zero);
                        continue;
                    }

                    if (IsHandleFormat(format)) continue;
                    byte[] bytes = ReadGlobal(data);
                    if (bytes != null) snapshot.Formats.Add(new KeyValuePair<uint, byte[]>(format, bytes));
                }
                return snapshot;
            }
            finally
            {
                Native.CloseClipboard();
            }
        }

        public static bool Restore(IntPtr owner, ClipboardSnapshot snapshot)
        {
            if (!Open(owner))
            {
                if (snapshot.EnhancedMetafile != IntPtr.Zero) Native.DeleteEnhMetaFile(snapshot.EnhancedMetafile);
                return false;
            }
            try
            {
                Native.EmptyClipboard();
                foreach (var format in snapshot.Formats) SetGlobal(format.Key, format.Value);

                // The clipboard takes ownership of the metafile once it is set.
                if (snapshot.EnhancedMetafile != IntPtr.Zero)
                    Native.SetClipboardData(Native.CF_ENHMETAFILE, snapshot.EnhancedMetafile);

                HideFromHistory();
                return true;
            }
            finally
            {
                Native.CloseClipboard();
            }
        }

        public static bool Clear(IntPtr owner)
        {
            if (!Open(owner)) return false;
            try { return Native.EmptyClipboard(); }
            finally { Native.CloseClipboard(); }
        }

        // Returns null if the clipboard has no text or couldn't be opened.
        public static string ReadText(IntPtr owner)
        {
            if (!Open(owner)) return null;
            try
            {
                IntPtr data = Native.GetClipboardData(Native.CF_UNICODETEXT);
                if (data == IntPtr.Zero) return null;
                IntPtr locked = Native.GlobalLock(data);
                if (locked == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(locked); }
                finally { Native.GlobalUnlock(data); }
            }
            finally
            {
                Native.CloseClipboard();
            }
        }

        public static bool WriteText(IntPtr owner, string text)
        {
            if (!Open(owner)) return false;
            try
            {
                Native.EmptyClipboard();
                bool written = SetGlobal(Native.CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + "\0"));
                HideFromHistory();
                return written;
            }
            finally
            {
                Native.CloseClipboard();
            }
        }

        static bool IsHandleFormat(uint format)
        {
            // These hold GDI handles or are owner-drawn, not memory. Windows rebuilds CF_BITMAP from CF_DIB.
            return format == Native.CF_BITMAP
                || format == Native.CF_METAFILEPICT
                || format == Native.CF_PALETTE
                || format == Native.CF_OWNERDISPLAY
                || (format >= Native.CF_DSPTEXT && format <= Native.CF_DSPENHMETAFILE)
                || (format >= Native.CF_GDIOBJFIRST && format <= Native.CF_GDIOBJLAST);
        }

        static byte[] ReadGlobal(IntPtr handle)
        {
            long size = (long)Native.GlobalSize(handle).ToUInt64();
            if (size <= 0 || size > MaxFormatBytes) return null;

            IntPtr locked = Native.GlobalLock(handle);
            if (locked == IntPtr.Zero) return null;
            try
            {
                var bytes = new byte[size];
                Marshal.Copy(locked, bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                Native.GlobalUnlock(handle);
            }
        }

        static bool SetGlobal(uint format, byte[] bytes)
        {
            IntPtr handle = Native.GlobalAlloc(Native.GMEM_MOVEABLE, new UIntPtr((ulong)bytes.Length));
            if (handle == IntPtr.Zero) return false;

            IntPtr locked = Native.GlobalLock(handle);
            if (locked == IntPtr.Zero)
            {
                Native.GlobalFree(handle);
                return false;
            }
            Marshal.Copy(bytes, 0, locked, bytes.Length);
            Native.GlobalUnlock(handle);

            if (Native.SetClipboardData(format, handle) != IntPtr.Zero) return true;
            Native.GlobalFree(handle);
            return false;
        }

        static void HideFromHistory()
        {
            var zero = new byte[4];
            SetGlobal(Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), zero);
            SetGlobal(Native.RegisterClipboardFormat("CanIncludeInClipboardHistory"), zero);
            SetGlobal(Native.RegisterClipboardFormat("CanUploadToCloudClipboard"), zero);
        }
    }
}
