using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats
{
    [Export(typeof(ImageFormat))]
    public class AvifImageFormat : ImageFormat, IDisposable
    {
        public override string         Tag { get { return "AVIF"; } }
        public override string Description { get { return "AV1 Image File Format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public AvifImageFormat()
        {
            Extensions = new[] { "avif" };
        }

        public enum avifResult
        {
            OK = 0,
            UnknownError = 1,
            InvalidFtyp = 2,
            NoContent = 3,
            NoYuvFormatSelected = 4,
            ReformatFailed = 5,
            UnsupportedDepth = 6,
            EncodeColorFailed = 7,
            EncodeAlphaFailed = 8,
            BmffParseFailed = 9,
            MissingImageItem = 10,
            DecodeColorFailed = 11,
            DecodeAlphaFailed = 12,
            ColorAlphaSizeMismatch = 13,
            IspeSizeMismatch = 14,
            NoCodecAvailable = 15,
            NoImagesRemaining = 16,
            InvalidExifPayload = 17,
            InvalidImageGrid = 18,
            InvalidCodecSpecificOption = 19,
            TruncatedData = 20,
            IoNotSet = 21,
            IoError = 22,
            WaitingOnIo = 23,
            InvalidArgument = 24,
            NotImplemented = 25,
            OutOfMemory = 26,
            CannotChangeSetting = 27,
            IncompatibleImage = 28,
            InternalError = 29,
            EncodeGainMapFailed = 30,
            DecodeGainMapFailed = 31,
            InvalidToneMappedImage = 32,
        }

        public enum avifRGBFormat
        {
            RGB = 0,
            RGBA,
            ARGB,
            BGR,
            BGRA,
            ABGR,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct avifRWData
        {
            public IntPtr data;
            public UIntPtr size;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct avifImage
        {
            public uint width;
            public uint height;
            public uint depth;
            public int yuvFormat;
            public int yuvRange;
            public int yuvChromaSamplePosition;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public IntPtr[] yuvPlanes;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public uint[] yuvRowBytes;
            public int imageOwnsYUVPlanes;
            public IntPtr alphaPlane;
            public uint alphaRowBytes;
            public int imageOwnsAlphaPlane;
            public int alphaPremultiplied;
            public avifRWData icc;
            public ushort colorPrimaries;
            public ushort transferCharacteristics;
            public ushort matrixCoefficients;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct avifRGBImage
        {
            public uint width;
            public uint height;
            public uint depth;
            public avifRGBFormat format;
            public int chromaUpsampling;
            public int chromaDownsampling;
            public int avoidLibYUV;
            public int ignoreAlpha;
            public int alphaPremultiplied;
            public int isFloat;
            public int maxThreads;
            public IntPtr pixels;
            public uint rowBytes;
        }

        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr avifDecoderCreate();
        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void avifDecoderDestroy(IntPtr decoder);
        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern avifResult avifDecoderReadMemory(IntPtr decoder, IntPtr image, byte[] data, UIntPtr size);
        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr avifImageCreateEmpty();
        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void avifImageDestroy(IntPtr image);
        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void avifRGBImageSetDefaults(ref avifRGBImage rgb, IntPtr image);
        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern avifResult avifImageYUVToRGB(IntPtr image, ref avifRGBImage rgb);
        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern avifResult avifRGBImageAllocatePixels(ref avifRGBImage rgb);
        [DllImport("avif.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void avifRGBImageFreePixels(ref avifRGBImage rgb);

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader(12);
            if (header.Length < 12 || !header.AsciiEqual(4, "ftyp"))
                return null;
            if (!header.AsciiEqual(8, "avif") && !header.AsciiEqual(8, "avis"))
                return null;

            IntPtr decoder = avifDecoderCreate();
            if (decoder == IntPtr.Zero)
                return null;
            IntPtr image = avifImageCreateEmpty();
            if (image == IntPtr.Zero)
            {
                avifDecoderDestroy(decoder);
                return null;
            }
            try
            {
                file.Position = 0;
                var input = file.ReadBytes((int)file.Length);
                var result = avifDecoderReadMemory(decoder, image, input, (UIntPtr)input.Length);
                if (result != avifResult.OK)
                    return null;

                var img_info = (avifImage)Marshal.PtrToStructure(image, typeof(avifImage));
                return new ImageMetaData
                {
                    Width = img_info.width,
                    Height = img_info.height,
                    BPP = img_info.alphaPlane != IntPtr.Zero ? 32 : 24,
                };
            }
            finally
            {
                avifImageDestroy(image);
                avifDecoderDestroy(decoder);
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            IntPtr decoder = avifDecoderCreate();
            if (decoder == IntPtr.Zero)
                throw new ApplicationException("avifDecoderCreate failed.");
            IntPtr image = avifImageCreateEmpty();
            if (image == IntPtr.Zero)
            {
                avifDecoderDestroy(decoder);
                throw new ApplicationException("avifImageCreateEmpty failed.");
            }
            try
            {
                file.Position = 0;
                var input = file.ReadBytes((int)file.Length);
                var result = avifDecoderReadMemory(decoder, image, input, (UIntPtr)input.Length);
                if (result != avifResult.OK)
                    throw new InvalidFormatException();

                var img_info = (avifImage)Marshal.PtrToStructure(image, typeof(avifImage));

                var rgb = new avifRGBImage();
                avifRGBImageSetDefaults(ref rgb, image);

                PixelFormat wpf_format;
                if (img_info.alphaPlane != IntPtr.Zero)
                {
                    rgb.format = avifRGBFormat.BGRA;
                    wpf_format = PixelFormats.Bgra32;
                }
                else
                {
                    rgb.format = avifRGBFormat.BGR;
                    wpf_format = PixelFormats.Bgr24;
                }
                rgb.depth = 8;

                if (avifResult.OK != avifRGBImageAllocatePixels(ref rgb))
                    throw new ApplicationException("avifRGBImageAllocatePixels failed.");
                try
                {
                    if (avifResult.OK != avifImageYUVToRGB(image, ref rgb))
                        throw new ApplicationException("avifImageYUVToRGB failed.");

                    int stride = (int)rgb.rowBytes;
                    var pixels = new byte[stride * rgb.height];
                    Marshal.Copy(rgb.pixels, pixels, 0, pixels.Length);

                    var bitmap = BitmapSource.Create((int)info.Width, (int)info.Height, 96, 96, wpf_format, null, pixels, stride);
                    bitmap.Freeze();
                    return new ImageData(bitmap, info);
                }
                finally
                {
                    avifRGBImageFreePixels(ref rgb);
                }
            }
            finally
            {
                avifImageDestroy(image);
                avifDecoderDestroy(decoder);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AvifImageFormat.Write not implemented");
        }

        public void Dispose ()
        {
        }
    }
}
