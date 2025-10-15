using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.InteropServices;
using GameRes;
using GameRes.Formats;

namespace GameRes.Formats
{
    /// <summary>
    /// UnArr archive file wrapper.
    /// </summary>
    public class UnArrArcFile : ArcFile
    {
        public IntPtr ArStream { get; private set; }
        public IntPtr ArArchive { get; private set; }

        public UnArrArcFile(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IntPtr arStream, IntPtr arArchive)
            : base(arc, impl, dir)
        {
            ArStream = arStream;
            ArArchive = arArchive;
        }

        protected override void Dispose(bool disposing)
        {
            if (ArArchive != IntPtr.Zero)
            {
                UnArr.ar_close_archive(ArArchive);
                ArArchive = IntPtr.Zero;
            }
            if (ArStream != IntPtr.Zero)
            {
                UnArr.ar_close(ArStream);
                ArStream = IntPtr.Zero;
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Base class for UnArr archive formats.
    /// </summary>
    public abstract class UnArrBaseArchive : ArchiveFormat
    {
        public override bool IsHierarchic { get { return true; } }

        public override ArcFile TryOpen(ArcView file)
        {
            var stream = file.CreateStream();
            var streamWrapper = new UnArr.StreamWrapper(stream);
            IntPtr arStream = IntPtr.Zero;
            IntPtr arArchive = IntPtr.Zero;

            try
            {
                arStream = UnArr.ar_open_istream(streamWrapper);
                if (arStream == IntPtr.Zero)
                    return null;

                arArchive = OpenArchive(arStream);
                if (arArchive == IntPtr.Zero)
                    return null;

                var dir = new List<Entry>();
                long offset = 0;

                while (UnArr.ar_parse_entry(arArchive))
                {
                    string name = UnArr.ArEntryGetName(arArchive);
                    if (string.IsNullOrEmpty(name))
                        continue;

                    var entry = new Entry
                    {
                        Name = name,
                        Type = FormatCatalog.Instance.GetTypeFromName(name, ContainedFormats),
                        Offset = offset,
                        Size = (uint)UnArr.ar_entry_get_size(arArchive).ToUInt64()
                    };
                    dir.Add(entry);
                    offset = UnArr.ar_entry_get_offset(arArchive);
                }

                if (dir.Count == 0)
                    return null;

                return new UnArrArcFile(file, this, dir, arStream, arArchive);
            }
            catch
            {
                if (arArchive != IntPtr.Zero)
                    UnArr.ar_close_archive(arArchive);
                if (arStream != IntPtr.Zero)
                    UnArr.ar_close(arStream);
                throw;
            }
        }

        /// <summary>
        /// Open archive from ar_stream. Must be implemented by derived classes.
        /// </summary>
        protected abstract IntPtr OpenArchive(IntPtr arStream);

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            var unarc = arc as UnArrArcFile;
            if (unarc == null)
                return base.OpenEntry(arc, entry);

            if (!UnArr.ar_parse_entry_at(unarc.ArArchive, entry.Offset))
                return Stream.Null;

            var size = UnArr.ar_entry_get_size(unarc.ArArchive);
            var oriSize = size.ToUInt64();

            var buffer = Marshal.AllocHGlobal((int)oriSize);
            try
            {
                if (!UnArr.ar_entry_uncompress(unarc.ArArchive, buffer, size))
                {
                    Marshal.FreeHGlobal(buffer);
                    return Stream.Null;
                }

                byte[] data = new byte[oriSize];
                Marshal.Copy(buffer, data, 0, (int)oriSize);
                Marshal.FreeHGlobal(buffer);

                return new MemoryStream(data, false);
            }
            catch
            {
                Marshal.FreeHGlobal(buffer);
                throw;
            }
        }
    }

    /// <summary>
    /// 7-Zip archive format implementation.
    /// </summary>
    [Export(typeof(ArchiveFormat))]
    public class SevenZipArchive : UnArrBaseArchive
    {
        public override string Tag { get { return "7Z"; } }
        public override string Description { get { return "7-Zip archive"; } }
        public override uint Signature { get { return 0; } }
        public override bool IsHierarchic { get { return true; } }
        public override bool CanWrite { get { return false; } }

        protected override IntPtr OpenArchive(IntPtr arStream)
        {
            return UnArr.ar_open_7z_archive(arStream);
        }
    }

    /// <summary>
    /// RAR archive format implementation.
    /// </summary>
    [Export(typeof(ArchiveFormat))]
    public class RarArchive : UnArrBaseArchive
    {
        public override string Tag { get { return "RAR"; } }
        public override string Description { get { return "RAR archive"; } }
        public override uint Signature { get { return 0; } } // 'Rar!'
        public override bool IsHierarchic { get { return true; } }
        public override bool CanWrite { get { return false; } }

        protected override IntPtr OpenArchive(IntPtr arStream)
        {
            return UnArr.ar_open_rar_archive(arStream);
        }
    }

    /// <summary>
    /// TAR archive format implementation.
    /// </summary>
    [Export(typeof(ArchiveFormat))]
    public class TarArchive : UnArrBaseArchive
    {
        public override string Tag { get { return "TAR"; } }
        public override string Description { get { return "TAR archive"; } }
        public override uint Signature { get { return 0; } } // TAR没有固定的魔术数字
        public override bool IsHierarchic { get { return true; } }
        public override bool CanWrite { get { return false; } }

        protected override IntPtr OpenArchive(IntPtr arStream)
        {
            return UnArr.ar_open_tar_archive(arStream);
        }

        public override ArcFile TryOpen(ArcView file)
        {
            // TAR文件通常在偏移257处有"ustar"标识
            if (file.MaxOffset > 0x105)
            {
                var signature = file.View.ReadBytes(0x101, 5);
                if (signature != null && System.Text.Encoding.ASCII.GetString(signature) == "ustar")
                    return base.TryOpen(file);
            }
            return null;
        }
    }

    /// <summary>
    /// ZIP archive format implementation.
    /// </summary>
    // [Export(typeof(ArchiveFormat))]
    // public class ZipArchive : UnArrBaseArchive
    // {
    //     public override string Tag { get { return "ZIP/UnArr"; } }
    //     public override string Description { get { return "ZIP archive (UnArr)"; } }
    //     public override uint Signature { get { return 0x04034b50; } } // PK\x03\x04
    //     public override bool IsHierarchic { get { return true; } }
    //     public override bool CanWrite { get { return false; } }

    //     protected override IntPtr OpenArchive(IntPtr arStream)
    //     {
    //         return UnArr.ar_open_zip_archive(arStream, false);
    //     }
    // }
}
