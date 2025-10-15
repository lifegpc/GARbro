using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices.ComTypes;

namespace GameRes.Formats {
    /// <summary>
    /// A tool to unpack zip/rar/7z/tar archives.
    /// </summary>
    public class UnArr {
        [ComVisible(true)]
        [ClassInterface(ClassInterfaceType.None)]
        public class StreamWrapper : IStream
        {
            private readonly Stream m_stream;

            public StreamWrapper(Stream stream)
            {
                m_stream = stream ?? throw new ArgumentNullException(nameof(stream));
            }

            public void Read(byte[] pv, int cb, IntPtr pcbRead)
            {
                int bytesRead = m_stream.Read(pv, 0, cb);
                if (pcbRead != IntPtr.Zero)
                {
                    Marshal.WriteInt32(pcbRead, bytesRead);
                }
            }

            public void Write(byte[] pv, int cb, IntPtr pcbWritten)
            {
                throw new NotSupportedException();
            }

            public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
            {
                long newPosition = m_stream.Seek(dlibMove, (SeekOrigin)dwOrigin);
                if (plibNewPosition != IntPtr.Zero)
                {
                    Marshal.WriteInt64(plibNewPosition, newPosition);
                }
            }

            public void SetSize(long libNewSize)
            {
                throw new NotSupportedException();
            }

            public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
            {
                throw new NotSupportedException();
            }

            public void Commit(int grfCommitFlags)
            {
                throw new NotSupportedException();
            }

            public void Revert()
            {
                throw new NotSupportedException();
            }

            public void LockRegion(long libOffset, long cb, int dwLockType)
            {
                throw new NotSupportedException();
            }

            public void UnlockRegion(long libOffset, long cb, int dwLockType)
            {
                throw new NotSupportedException();
            }

            public void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG pstatstg, int grfStatFlag)
            {
                pstatstg = new System.Runtime.InteropServices.ComTypes.STATSTG();
                pstatstg.type = 2; // STGTY_STREAM
                pstatstg.cbSize = m_stream.Length;
            }

            public void Clone(out IStream ppstm)
            {
                ppstm = null;
                throw new NotSupportedException();
            }
        }

        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // opens a read-only stream based on the given IStream
        // ar_stream *ar_open_istream(IStream *stream);
        public static extern IntPtr ar_open_istream([In, MarshalAs(UnmanagedType.Interface)] IStream stream);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // closes the stream and releases underlying resources
        // void ar_close(ar_stream *stream);
        public static extern void ar_close(IntPtr stream);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // tries to read 'count' bytes into buffer, advancing the read offset pointer; returns the actual number of bytes read
        // size_t ar_read(ar_stream *stream, void *buffer, size_t count);
        public static extern UIntPtr ar_read(IntPtr stream, IntPtr buffer, UIntPtr count);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        // moves the read offset pointer (same as fseek); returns false on failure
        // bool ar_seek(ar_stream *stream, off64_t offset, int origin);
        public static extern bool ar_seek(IntPtr stream, long offset, int origin);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        // shortcut for ar_seek(stream, count, SEEK_CUR); returns false on failure
        // bool ar_skip(ar_stream *stream, off64_t count);
        public static extern bool ar_skip(IntPtr stream, long count);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // returns the current read offset (or 0 on error)
        // off64_t ar_tell(ar_stream *stream);
        public static extern long ar_tell(IntPtr stream);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // frees all data stored for the given archive; does not close the underlying stream
        // void ar_close_archive(ar_archive *ar);
        public static extern void ar_close_archive(IntPtr ar);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // reads the next archive entry; returns false on error or at the end of the file (use ar_at_eof to distinguish the two cases)
        // bool ar_parse_entry(ar_archive *ar);
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool ar_parse_entry(IntPtr ar);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // reads the archive entry at the given offset as returned by ar_entry_get_offset (offset 0 always restarts at the first entry); should always succeed
        // bool ar_parse_entry_at(ar_archive *ar, off64_t offset)
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool ar_parse_entry_at(IntPtr ar, long offset);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // reads the (first) archive entry associated with the given name; returns false if the entry couldn't be found
        // entry_name should be UTF-8 encoded
        // bool ar_parse_entry_for(ar_archive *ar, const char *entry_name);
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool ar_parse_entry_for(IntPtr ar, IntPtr entry_name);
        public static bool ArParseEntryFor(IntPtr ar, string entry_name) {
            var bytes = Encoding.UTF8.GetBytes(entry_name);
            var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            Marshal.WriteByte(ptr, bytes.Length, 0);
            var result = ar_parse_entry_for(ar, ptr);
            Marshal.FreeHGlobal(ptr);
            return result;
        }
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // returns whether the last ar_parse_entry call has reached the file's expected end
        // bool ar_at_eof(ar_archive *ar);
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool ar_at_eof(IntPtr ar);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // returns the name of the current entry as UTF-8 string; this pointer is only valid until the next call to ar_parse_entry; returns NULL on failure
        // const char *ar_entry_get_name(ar_archive *ar);
        public static extern IntPtr ar_entry_get_name(IntPtr ar);
        public static string ArEntryGetName(IntPtr ar) {
            var ptr = ar_entry_get_name(ar);
            if (ptr == IntPtr.Zero)
                return null;
            int len = 0;
            while (Marshal.ReadByte(ptr, len) != 0) ++len;
            byte[] buffer = new byte[len];
            Marshal.Copy(ptr, buffer, 0, len);
            return Encoding.UTF8.GetString(buffer);
        }
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // returns the stream offset of the current entry for use with ar_parse_entry_at
        // off64_t ar_entry_get_offset(ar_archive *ar);
        public static extern long ar_entry_get_offset(IntPtr ar);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // returns the total size of uncompressed data of the current entry; read exactly that many bytes using ar_entry_uncompress
        // size_t ar_entry_get_size(ar_archive *ar);
        public static extern UIntPtr ar_entry_get_size(IntPtr ar);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // WARNING: don't manually seek in the stream between ar_parse_entry and the last corresponding ar_entry_uncompress call!
        // uncompresses the next 'count' bytes of the current entry into buffer; returns false on error
        // bool ar_entry_uncompress(ar_archive *ar, void *buffer, size_t count);
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool ar_entry_uncompress(IntPtr ar, IntPtr buffer, UIntPtr count);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // checks whether 'stream' could contain RAR data and prepares for archive listing/extraction; returns NULL on failure
        // ar_archive *ar_open_rar_archive(ar_stream *stream);
        public static extern IntPtr ar_open_rar_archive(IntPtr stream);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // checks whether 'stream' could contain TAR data and prepares for archive listing/extraction; returns NULL on failure
        // ar_archive *ar_open_tar_archive(ar_stream *stream);
        public static extern IntPtr ar_open_tar_archive(IntPtr stream);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // checks whether 'stream' could contain ZIP data and prepares for archive listing/extraction; returns NULL on failure
        // set deflatedonly for extracting XPS, EPUB, etc. documents where non-Deflate compression methods are not supported by specification
        // ar_archive *ar_open_zip_archive(ar_stream *stream, bool deflatedonly);
        public static extern IntPtr ar_open_zip_archive(IntPtr stream, [MarshalAs(UnmanagedType.U1)] bool deflatedonly);
        [DllImport("unarr.dll", CallingConvention = CallingConvention.Cdecl)]
        // checks whether 'stream' could contain 7Z data and prepares for archive listing/extraction; returns NULL on failure
        // ar_archive *ar_open_7z_archive(ar_stream *stream);
        public static extern IntPtr ar_open_7z_archive(IntPtr stream);
    }
}
