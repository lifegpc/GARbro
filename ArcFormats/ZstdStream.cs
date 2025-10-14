using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GameRes.Compression
{
    public static class ZstdReader
    {
        private const int BUFFER_SIZE = 4096;

        [DllImport("zstd-1.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ZSTD_createDStream();

        [DllImport("zstd-1.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ZSTD_decompressStream(IntPtr zds, ref ZSTD_outBuffer output, ref ZSTD_inBuffer input);

        [DllImport("zstd-1.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ZSTD_freeDStream(IntPtr zds);

        [StructLayout(LayoutKind.Sequential)]
        private struct ZSTD_inBuffer
        {
            public IntPtr src;
            public UIntPtr size;
            public UIntPtr pos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ZSTD_outBuffer
        {
            public IntPtr dst;
            public UIntPtr size;
            public UIntPtr pos;
        }

        public static byte[] Unpack(Stream compressedStream)
        {
            if (compressedStream == null) throw new ArgumentNullException(nameof(compressedStream));

            IntPtr dctx = ZSTD_createDStream();
            if (dctx == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create ZSTD_DStream.");

            byte[] inBuf = new byte[BUFFER_SIZE];
            byte[] outBuf = new byte[BUFFER_SIZE];
            using (MemoryStream result = new MemoryStream())
            {
                try
                {
                    ZSTD_inBuffer input = new ZSTD_inBuffer();
                    ZSTD_outBuffer output = new ZSTD_outBuffer();

                    GCHandle inHandle = default, outHandle = default;
                    try
                    {
                        inHandle = GCHandle.Alloc(inBuf, GCHandleType.Pinned);
                        outHandle = GCHandle.Alloc(outBuf, GCHandleType.Pinned);

                        int lastRet;
                        while (true)
                        {
                            int bytesRead = compressedStream.Read(inBuf, 0, inBuf.Length);
                            if (bytesRead == 0) break;

                            input.src = inHandle.AddrOfPinnedObject();
                            input.size = (UIntPtr)(uint)bytesRead;
                            input.pos = UIntPtr.Zero;

                            while (input.pos.ToUInt64() < input.size.ToUInt64())
                            {
                                output.dst = outHandle.AddrOfPinnedObject();
                                output.size = (UIntPtr)(uint)outBuf.Length;
                                output.pos = UIntPtr.Zero;

                                lastRet = ZSTD_decompressStream(dctx, ref output, ref input);
                                if (lastRet < 0)
                                    throw new InvalidDataException($"ZSTD_decompressStream error: {lastRet}");

                                if (output.pos.ToUInt64() > 0)
                                    result.Write(outBuf, 0, (int)output.pos.ToUInt64());

                                if (lastRet == 0) break;
                            }
                        }
                    }
                    finally
                    {
                        if (inHandle.IsAllocated) inHandle.Free();
                        if (outHandle.IsAllocated) outHandle.Free();
                    }

                    return result.ToArray();
                }
                finally
                {
                    ZSTD_freeDStream(dctx);
                }
            }
        }
    }
}
