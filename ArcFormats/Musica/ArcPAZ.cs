//! PAZ archive implementation.
//
// Copyright (C) 2016 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using GameRes.Compression;
using GameRes.Cryptography;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.Musica
{
    internal class PazEntry : PackedEntry
    {
        public uint     AlignedSize;
        public byte[]   Key;
    }

    internal abstract class PazArchiveBase : MultiFileArchive
    {
        public readonly int         Version;
        public readonly byte        XorKey;

        public PazArchiveBase (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, int version, byte key, IReadOnlyList<ArcView> parts = null)
            : base (arc, impl, dir, parts)
        {
            Version = version;
            XorKey = key;
        }

        protected override uint GetEntrySize (Entry entry)
        {
            var pent = entry as PazEntry;
            if (pent != null)
                return pent.AlignedSize;
            else
                return entry.Size;
        }

        internal abstract Stream DecryptEntry (Stream input, PazEntry entry);
    }

    internal class PazArchive : PazArchiveBase
    {
        public readonly Blowfish    Encryption;

        public PazArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, int version, byte key, byte[] data_key, IReadOnlyList<ArcView> parts = null)
            : base (arc, impl, dir, version, key, parts)
        {
            Encryption = new Blowfish (data_key);
        }

        internal override Stream DecryptEntry (Stream input, PazEntry entry)
        {
            input = new InputCryptoStream (input, Encryption.CreateDecryptor());
            var key = entry.Key;
            if (null == key)
                return input;
            var rc4 = new Rc4Transform (key);
            if (Version >= 2)
            {
                uint crc = Crc32.Compute (key, 0, key.Length);
                int skip_rounds = (int)(crc >> 12) & 0xFF; 
                for (int i = 0; i < skip_rounds; ++i)
                {
                    rc4.NextByte();
                }
            }
            return new InputCryptoStream (input, rc4);
        }
    }

    internal class MovPazArchive : PazArchiveBase
    {
        public readonly byte[]  MovKey;

        public MovPazArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, int version, byte key, byte[] mov_key, IReadOnlyList<ArcView> parts = null)
            : base (arc, impl, dir, version, key, parts)
        {
            MovKey = mov_key;
        }

        internal override Stream DecryptEntry (Stream input, PazEntry entry)
        {
            if (Version < 1)
            {
                using (input)
                {
                    var data = new byte[entry.AlignedSize];
                    input.Read (data, 0, data.Length);
                    for (int i = 0; i < data.Length; ++i)
                        data[i] = MovKey[data[i]];
                    return new BinMemoryStream (data, entry.Name);
                }
            }
            var key = new byte[0x100];
            for (int i = 0; i < 0x100; ++i)
                key[i] = (byte)(MovKey[i] ^ entry.Key[i % entry.Key.Length]);

            var rc4 = new Rc4Transform (key);
            var block = rc4.GenerateBlock ((int)Math.Min (0x10000, input.Length));
            return new ByteStringEncryptedStream (input, block);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PazOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAZ"; } }
        public override string Description { get { return "Musica engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return true; } }

        public PazOpener ()
        {
            Extensions = new string[] { "paz", "dat" };
            Signatures = new uint[] {
                0x858F8493, 0x8F889395, 0x6E656465, 0x848F8486, 0x61657453, 0x6873616D, 0x92808483,
                0x6E697274, 0
            };
            ContainedFormats = new string[] { "PNG", "ANI/PAZ", "SQZ", "OGG", "WAV", "TXT" };
        }

        static readonly ISet<string> AudioPazNames = new HashSet<string> {
            "bgm", "se", "voice", "pmbgm", "pmse", "pmvoice"
        };
        static readonly ISet<string> VideoPazNames = new HashSet<string> { "mov" };

        public override ArcFile TryOpen (ArcView file)
        {
            uint signature = file.View.ReadUInt32 (0);
            var scheme = QueryEncryption (file.Name, signature);
            if (null == scheme)
                return null;
            uint start_offset = scheme.Version > 0 ? 0x20u : 0u;
            uint index_size = file.View.ReadUInt32 (start_offset);
            start_offset += 4;
            byte xor_key = (byte)(index_size >> 24);
            if (xor_key != 0)
                index_size ^= (uint)(xor_key << 24 | xor_key << 16 | xor_key << 8 | xor_key);
            if (0 != (index_size & 7) || index_size + start_offset >= file.MaxOffset)
                return null;

            var arc_list = new List<Entry>();
            var arc_dir = VFS.GetDirectoryName (file.Name);
            long max_offset = file.MaxOffset;
            for (char suffix = 'A'; suffix <= 'Z'; ++suffix)
            {
                var part_name = VFS.CombinePath (arc_dir, file.Name + suffix);
                if (!VFS.FileExists (part_name))
                    break;
                var part = VFS.FindFile (part_name);
                arc_list.Add (part);
                max_offset += part.Size;
            }
            var arc_name = Path.GetFileNameWithoutExtension (file.Name).ToLowerInvariant();
            bool is_audio = AudioPazNames.Contains (arc_name);
            bool is_video = VideoPazNames.Contains (arc_name);
            Stream input = file.CreateStream (start_offset, index_size);
            byte[] video_key = null;
            List<Entry> dir;
            try
            {
                if (xor_key != 0)
                    input = new XoredStream (input, xor_key);
                var enc = new Blowfish (scheme.ArcKeys[arc_name].IndexKey);
                input = new InputCryptoStream (input, enc.CreateDecryptor());
                using (var index = new ArcView.Reader (input))
                {
                    int count = index.ReadInt32();
                    if (!IsSaneCount (count))
                        return null;
                    if (is_video)
                        video_key = index.ReadBytes (0x100);

                    dir = new List<Entry> (count);
                    for (int i = 0; i < count; ++i)
                    {
                        var name = index.BaseStream.ReadCString();
                        var entry = FormatCatalog.Instance.Create<PazEntry> (name);
                        entry.Offset    = index.ReadInt64();
                        entry.UnpackedSize = index.ReadUInt32();
                        entry.Size        = index.ReadUInt32();
                        entry.AlignedSize = index.ReadUInt32();
                        if (!entry.CheckPlacement (max_offset))
                            return null;
                        entry.IsPacked = index.ReadInt32 () != 0;
                        if (string.IsNullOrEmpty (entry.Type) && is_audio)
                        {
                            entry.Type = "audio";
                        }
                        if (scheme.Version > 0)
                        {
                            string password = "";
                            if (!entry.IsPacked && scheme.TypeKeys != null)
                            {
                                password = scheme.GetTypePassword (name, is_audio);
                            }
                            if (!string.IsNullOrEmpty (password) || is_video)
                            {
                                password = string.Format ("{0} {1:X08} {2}", name.ToLowerInvariant(), entry.UnpackedSize, password);
                                entry.Key = Encodings.cp932.GetBytes (password);
                            }
                        }
                        dir.Add (entry);
                    }
                }
            }
            finally
            {
                input.Dispose();
            }
            List<ArcView> parts = null;
            if (arc_list.Count > 0)
            {
                parts = new List<ArcView> (arc_list.Count);
                try
                {
                    foreach (var arc_entry in arc_list)
                    {
                        var arc_file = VFS.OpenView (arc_entry);
                        parts.Add (arc_file);
                    }
                }
                catch
                {
                    foreach (var part in parts)
                        part.Dispose();
                    throw;
                }
            }
            if (is_video)
            {
                if (scheme.Version < 1)
                {
                    var table = new byte[0x100];
                    for (int i = 0; i < 0x100; ++i)
                        table[video_key[i]] = (byte)i;
                    video_key = table;
                }
                return new MovPazArchive (file, this, dir, scheme.Version, xor_key, video_key, parts);
            }
            return new PazArchive (file, this, dir, scheme.Version, xor_key, scheme.ArcKeys[arc_name].DataKey, parts);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var parc = arc as PazArchiveBase;
            var pent = entry as PazEntry;
            if (null == parc || null == pent)
                return base.OpenEntry (arc, entry);

            Stream input = parc.OpenStream (entry);
            try
            {
                if (parc.XorKey != 0)
                    input = new XoredStream (input, parc.XorKey);

                input = parc.DecryptEntry (input, pent);

                if (pent.Size < pent.AlignedSize)
                    input = new LimitStream (input, pent.Size);
                if (pent.IsPacked)
                    input = new ZLibStream (input, CompressionMode.Decompress);
                return input;
            }
            catch
            {
                if (input != null)
                    input.Dispose();
                throw;
            }
        }

        PazScheme QueryEncryption (string arc_name, uint signature)
        {
            PazScheme scheme = null;
            if (!KnownSchemes.TryGetValue (signature, out scheme) && KnownTitles.Count > 1)
            {
                var title = FormatCatalog.Instance.LookupGame (arc_name);
                scheme = GetScheme (title);
                if (null == scheme)
                {
                    if (!arc_name.HasExtension (".paz"))
                        return null;
                    var options = Query<PazOptions> (arcStrings.ArcEncryptedNotice);
                    scheme = options.Scheme;
                }
            }
            arc_name = Path.GetFileNameWithoutExtension (arc_name).ToLowerInvariant();
            if (null == scheme || !scheme.ArcKeys.ContainsKey (arc_name))
                throw new UnknownEncryptionScheme();
            return scheme;
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new PazOptions {
                Scheme            = GetScheme (Properties.Settings.Default.PAZTitle),
                ArcName           = Properties.Settings.Default.PAZArchiveKey,
                CompressContents  = Properties.Settings.Default.PAZCompressContents,
                RetainDirs        = Properties.Settings.Default.PAZRetainStructure,
                Signature         = 0,
            };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            var create = widget as GUI.CreatePAZWidget;
            if (null == create)
                return base.GetDefaultOptions ();

            string title = create.SelectedTitle ?? string.Empty;
            string archive = create.SelectedArchive ?? string.Empty;
            bool compress = create.CompressContents;
            bool retain = create.RetainStructure;

            Properties.Settings.Default.PAZTitle = title;
            Properties.Settings.Default.PAZArchiveKey = archive;
            Properties.Settings.Default.PAZCompressContents = compress;
            Properties.Settings.Default.PAZRetainStructure = retain;

            var options = new PazOptions {
                Scheme            = GetScheme (title),
                ArcName           = archive,
                CompressContents  = compress,
                RetainDirs        = retain,
                Signature         = 0,
            };
            return options;
        }

        public override object GetCreationWidget ()
        {
            return new GUI.CreatePAZWidget (this);
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetPAZ (this);
        }

        public override void Create (Stream output, IEnumerable<Entry> list, ResourceOptions options,
                                     EntryCallback callback)
        {
            if (null == output)
                throw new ArgumentNullException ("output");

            var paz_options = GetOptions<PazOptions> (options);
            var scheme = paz_options.Scheme;
            string arc_name = paz_options.ArcName ?? string.Empty;
            PazKey keys = null;

            bool use_crypto = scheme != null;
            if (use_crypto)
            {
                if (string.IsNullOrEmpty (arc_name))
                    throw new InvalidOperationException ("Archive key is not specified.");
                arc_name = arc_name.ToLowerInvariant ();
                if (null == scheme.ArcKeys || !scheme.ArcKeys.TryGetValue (arc_name, out keys))
                    throw new InvalidOperationException (string.Format ("Unknown archive key '{0}'.", arc_name));
                if (VideoPazNames.Contains (arc_name))
                    throw new NotSupportedException ("Creation of MOV PAZ archives is not supported.");
            }
            else
            {
                arc_name = string.Empty;
            }

            bool is_audio = use_crypto && AudioPazNames.Contains (arc_name);
            bool retain_dirs = paz_options.RetainDirs;
            bool compress_contents = paz_options.CompressContents;
            byte xor_key = 0;

            var encoding = Encodings.cp932.WithFatalFallback ();
            var data_key = use_crypto ? keys.DataKey : null;
            var index_key = use_crypto ? keys.IndexKey : null;
            int version = scheme != null ? scheme.Version : 0;

            var entries = new List<PazCreateEntry>();
            var used_names = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
            string temp_path = Path.GetTempFileName ();
            long data_offset = 0;
            int callback_index = 0;

            try
            {
                using (var data_stream = new FileStream (temp_path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    foreach (var entry in list)
                    {
                        string stored_name = retain_dirs ? entry.Name.Replace ("\\", "/") : Path.GetFileName (entry.Name);
                        if (string.IsNullOrWhiteSpace (stored_name))
                            continue;
                        if (!used_names.Add (stored_name))
                            continue;

                        if (null != callback)
                            callback (callback_index++, entry, arcStrings.MsgAddingFile);

                        var created = CreateEntryData (entry, stored_name, data_stream, ref data_offset,
                                                       compress_contents, is_audio, scheme, data_key, xor_key, encoding);
                        entries.Add (created);
                    }
                }

                if (null != callback)
                    callback (callback_index++, null, arcStrings.MsgWritingIndex);

                long plain_index_size = CalculateIndexPlainSize (entries);
                int aligned_index_size = (int)((plain_index_size + 7) & ~7);
                uint index_size = (uint)aligned_index_size;
                uint start_offset = version > 0 ? 0x20u : 0u;
                long data_start = start_offset + 4 + aligned_index_size;

                foreach (var item in entries)
                    item.Offset = data_start + item.DataOffset;

                var index_buffer = BuildIndexBuffer (entries, aligned_index_size, index_key, xor_key);
                uint stored_index_size = EncodeIndexSize (index_size, xor_key);

                uint signature = version > 0 ? ResolveSignature (scheme, paz_options.Signature) : 0u;

                output.Position = 0;
                using (var writer = new BinaryWriter (output, Encoding.ASCII, true))
                {
                    if (start_offset != 0)
                    {
                        writer.Write (signature);
                        if (start_offset > 4)
                            writer.Write (new byte[start_offset - 4]);
                    }
                    writer.Write (stored_index_size);
                    writer.Write (index_buffer);
                }

                output.Position = data_start;
                using (var data_stream = new FileStream (temp_path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    data_stream.CopyTo (output);
                }
            }
            finally
            {
                try { File.Delete (temp_path); } catch { }
            }
        }

        static long CalculateIndexPlainSize (IList<PazCreateEntry> entries)
        {
            long size = 4;
            foreach (var entry in entries)
                size += entry.NameBytes.Length + 1 + 8 + 4 + 4 + 4 + 4;
            return size;
        }

        byte[] BuildIndexBuffer (IList<PazCreateEntry> entries, int aligned_size, byte[] index_key, byte xor_key)
        {
            var index_stream = new MemoryStream (Math.Max (aligned_size, 8));
            using (var writer = new BinaryWriter (index_stream, Encoding.ASCII, true))
            {
                writer.Write (entries.Count);
                foreach (var entry in entries)
                {
                    writer.Write (entry.NameBytes);
                    writer.Write ((byte)0);
                    writer.Write (entry.Offset);
                    writer.Write (entry.UnpackedSize);
                    writer.Write (entry.Size);
                    writer.Write (entry.AlignedSize);
                    writer.Write (entry.IsPacked ? 1 : 0);
                }
                writer.Flush ();
            }
            if (index_stream.Length < aligned_size)
                index_stream.SetLength (aligned_size);
            var buffer = index_stream.ToArray ();
            if (buffer.Length > 0 && null != index_key)
            {
                var crypto = new Blowfish (index_key);
                crypto.Encipher (buffer, buffer.Length);
            }
            if (xor_key != 0)
            {
                for (int i = 0; i < buffer.Length; ++i)
                    buffer[i] ^= xor_key;
            }
            return buffer;
        }

        static uint EncodeIndexSize (uint index_size, byte xor_key)
        {
            if (0 == xor_key)
                return index_size;
            uint mask = (uint)(xor_key << 24 | xor_key << 16 | xor_key << 8 | xor_key);
            return index_size ^ mask;
        }

        uint ResolveSignature (PazScheme scheme, uint explicit_value)
        {
            if (explicit_value != 0)
                return explicit_value;
            foreach (var pair in KnownSchemes)
            {
                if (object.ReferenceEquals (pair.Value, scheme))
                    return pair.Key;
            }
            return 0u;
        }

        PazCreateEntry CreateEntryData (Entry entry, string stored_name, FileStream data_stream, ref long data_offset,
                                        bool compress_contents, bool is_audio, PazScheme scheme, byte[] data_key,
                                        byte xor_key, Encoding encoding)
        {
            string file_path = entry.Name;
            using (var input = File.Open (file_path, FileMode.Open, FileAccess.Read))
            {
                long file_length = input.Length;
                if (file_length < 0 || file_length > uint.MaxValue)
                    throw new FileSizeException (entry.Name);
                if (file_length > int.MaxValue)
                    throw new FileSizeException (entry.Name);

                var raw = new byte[file_length];
                int read = 0;
                while (read < raw.Length)
                {
                    int chunk = input.Read (raw, read, raw.Length - read);
                    if (0 == chunk)
                        break;
                    read += chunk;
                }
                if (read != raw.Length)
                    throw new EndOfStreamException (entry.Name);

                bool should_compress = compress_contents && ShouldCompressFile (entry) && raw.Length > 0;
                byte[] payload = raw;
                bool is_packed = false;
                if (should_compress)
                {
                    using (var packed_stream = new MemoryStream (raw.Length))
                    {
                        using (var zstream = new ZLibStream (packed_stream, CompressionMode.Compress, CompressionLevel.Level9, true))
                        {
                            zstream.Write (raw, 0, raw.Length);
                        }
                        var packed = packed_stream.ToArray ();
                        if (packed.Length < raw.Length)
                        {
                            payload = packed;
                            is_packed = true;
                        }
                    }
                }

                var info = new PazCreateEntry ();
                info.Name = stored_name;
                try
                {
                    info.NameBytes = encoding.GetBytes (stored_name);
                }
                catch (EncoderFallbackException X)
                {
                    throw new InvalidFileName (stored_name, arcStrings.MsgIllegalCharacters, X);
                }

                info.UnpackedSize = (uint)raw.Length;
                info.Size = (uint)payload.Length;
                info.IsPacked = is_packed;

                byte[] aligned;
                if (0 == (payload.Length & 7))
                {
                    aligned = (byte[])payload.Clone ();
                }
                else
                {
                    int aligned_size = (payload.Length + 7) & ~7;
                    aligned = new byte[aligned_size];
                    Buffer.BlockCopy (payload, 0, aligned, 0, payload.Length);
                }

                int version = scheme != null ? scheme.Version : 0;
                if (version > 0)
                {
                    string password = string.Empty;
                    if (!is_packed && scheme != null && scheme.TypeKeys != null)
                        password = scheme.GetTypePassword (stored_name, is_audio);
                    if (!string.IsNullOrEmpty (password))
                    {
                        string key_string = string.Format ("{0} {1:X08} {2}", stored_name.ToLowerInvariant(), info.UnpackedSize, password);
                        var key_bytes = Encodings.cp932.GetBytes (key_string);
                        var rc4 = new Rc4Transform (key_bytes);
                        if (version >= 2)
                        {
                            uint crc = Crc32.Compute (key_bytes, 0, key_bytes.Length);
                            int skip = (int)(crc >> 12) & 0xFF;
                            for (int i = 0; i < skip; ++i)
                                rc4.NextByte ();
                        }
                        rc4.TransformBlock (aligned, 0, payload.Length, aligned, 0);
                    }
                }

                if (aligned.Length > 0 && null != data_key)
                {
                    var crypto = new Blowfish (data_key);
                    crypto.Encipher (aligned, aligned.Length);
                }

                if (xor_key != 0)
                {
                    for (int i = 0; i < aligned.Length; ++i)
                        aligned[i] ^= xor_key;
                }

                info.AlignedSize = (uint)aligned.Length;
                info.DataOffset = data_offset;

                data_stream.Position = data_offset;
                data_stream.Write (aligned, 0, aligned.Length);
                data_offset += aligned.Length;

                return info;
            }
        }

        bool ShouldCompressFile (Entry entry)
        {
            if ("image" == entry.Type || "archive" == entry.Type)
                return false;
            if (entry.Name.HasExtension (".ogg"))
                return false;
            return true;
        }

        class PazCreateEntry
        {
            public string   Name;
            public byte[]   NameBytes;
            public long     DataOffset;
            public long     Offset;
            public uint     UnpackedSize;
            public uint     Size;
            public uint     AlignedSize;
            public bool     IsPacked;
        }

        PazScheme GetScheme (string title)
        {
            PazScheme scheme;
            if (string.IsNullOrEmpty (title) || !KnownTitles.TryGetValue (title, out scheme))
                return null;
            return scheme;
        }

        public override ResourceScheme Scheme
        {
            get { return m_scheme; }
            set { m_scheme = (MusicaScheme)value; }
        }

        MusicaScheme m_scheme = new MusicaScheme
        {
            KnownSchemes = new Dictionary<uint, PazScheme>(),
            KnownTitles = new Dictionary<string, PazScheme>()
        };

        public IDictionary<uint, PazScheme>  KnownSchemes { get { return m_scheme.KnownSchemes; } }
        public IDictionary<string, PazScheme> KnownTitles { get { return m_scheme.KnownTitles; } }
    }

    [Serializable]
    public class PazKey
    {
        public byte[]   IndexKey;
        public byte[]   DataKey;
    }

    [Serializable]
    public class PazScheme
    {
        public int                          Version;
        public IDictionary<string, PazKey>  ArcKeys;
        public IDictionary<string, string>  TypeKeys;

        public string GetTypePassword (string name, bool is_audio)
        {
            string password = null;
            if (name.Contains ('.'))
            {
                if (name.EndsWith (".png"))
                    TypeKeys.TryGetValue ("png", out password);
                else if (name.EndsWith (".ogg") || is_audio)
                    TypeKeys.TryGetValue ("ogg", out password);
                else if (name.EndsWith (".sc"))
                    TypeKeys.TryGetValue ("sc", out password);
                else if (name.EndsWith (".avi") || name.EndsWith (".mpg") || name.EndsWith (".mpeg"))
                    TypeKeys.TryGetValue ("avi", out password);
            }
            else if (is_audio)
                TypeKeys.TryGetValue ("ogg", out password);
            return password ?? "";
        }
    }

    [Serializable]
    public class MusicaScheme : ResourceScheme
    {
        public IDictionary<uint, PazScheme>     KnownSchemes;
        public IDictionary<string, PazScheme>   KnownTitles;
    }

    public class PazOptions : ResourceOptions
    {
        public PazScheme    Scheme;
        public string       ArcName;
        public bool         CompressContents;
        public bool         RetainDirs;
        public uint         Signature;
    }
}
