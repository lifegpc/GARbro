using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats
{
    [Export(typeof(ImageFormat))]
    public class JxlImageFormat : ImageFormat, IDisposable
    {
        public override string         Tag { get { return "JXL"; } }
        public override string Description { get { return "JPEG XL image format"; } }
        public override uint     Signature { get { return 0; } } // JXL signature starts with 0xFF 0x0A
        public override bool      CanWrite { get { return false; } }

        public JxlImageFormat()
        {
            Extensions = new[] { "jxl" };
        }

        public enum JxlDec {
            Success = 0,
            Error = 1,
            NeedMoreInput = 2,
            NeedPreviewOutBuffer = 3,
            NeedImageOutBuffer = 5,
            JpegNeedMoreOutput = 6,
            BoxNeedMoreOutput = 7,
            BasicInfo = 0x40,
            ColorEncoding = 0x100,
            PreviewImage = 0x200,
            Frame = 0x400,
            FullImage = 0x1000,
            JpegReconstruction = 0x2000,
            Box = 0x4000,
            FrameProgression = 0x8000,
            Complete = 0x10000,
        }

        public enum JxlOrientation {
            Identity = 1,
            FlipHorizontal = 2,
            Rotate = 3,
            FlipVertical = 4,
            Transpose = 5,
            Rotate90CW = 6,
            AntiTranspose = 7,
            Rotate90CCW = 8,
        }

        public enum JxlDataType {
            Float = 0,
            Uint8 = 2,
            Uint16 = 3,
            Float16 = 5,
        }

        public enum JxlEndianness {
            Native = 0,
            Little = 1,
            Big = 2,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JxlPixelFormat {
            public uint num_channels;
            public JxlDataType data_type;
            public JxlEndianness endianness;
            public UIntPtr align;
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct JxlBasicInfo {
            public int have_container;
            public uint xsize;
            public uint ysize;
            public uint bits_per_sample;
            public uint exponent_bits_per_sample;
            public float intensity_target;
            public float min_nits;
            public int relative_to_max_display;
            public float linear_below;
            public int uses_original_profile;
            public int have_preview;
            public int have_animation;
            public JxlOrientation orientation;
            public uint num_color_channels;
            public uint num_extra_channels;
            public uint alpha_bits;
            public uint alpha_exponent_bits;
            public int alpha_premultiplied;
            public uint preview_xsize;
            public uint preview_ysize;
            public uint animation_tps_numerator;
            public uint animation_tps_denominator;
            public uint animation_num_loops;
            public int animation_have_timecodes;
            public uint intrinsic_xsize;
            public uint intrinsic_ysize;
            fixed byte padding[100];
        }

        [DllImport("jxl_dec.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr JxlDecoderCreate(IntPtr memoryManager);
        [DllImport("jxl_dec.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void JxlDecoderDestroy(IntPtr dec);
        [DllImport("jxl_dec.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern JxlDec JxlDecoderSubscribeEvents(IntPtr dec, uint events);
        [DllImport("jxl_dec.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern JxlDec JxlDecoderSetInput(IntPtr dec, byte[] data, UIntPtr size);
        [DllImport("jxl_dec.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern JxlDec JxlDecoderProcessInput(IntPtr dec);
        [DllImport("jxl_dec.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern JxlDec JxlDecoderGetBasicInfo(IntPtr dec, ref JxlBasicInfo info);
        [DllImport("jxl_dec.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern JxlDec JxlDecoderImageOutBufferSize(IntPtr dec, ref JxlPixelFormat format, ref UIntPtr size);
        [DllImport("jxl_dec.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern JxlDec JxlDecoderSetImageOutBuffer(IntPtr dec, ref JxlPixelFormat format, IntPtr buffer, UIntPtr size);
        [DllImport("jxl_dec.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void JxlDecoderCloseInput(IntPtr dec);

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (2);
            if (header[0] != 0xFF || header[1] != 0x0A)
                return null;

            IntPtr dec = JxlDecoderCreate(IntPtr.Zero);
            if (dec == IntPtr.Zero)
                return null;
            try
            {
                if (JxlDec.Success != JxlDecoderSubscribeEvents(dec, (uint)JxlDec.BasicInfo))
                    return null;

                file.Position = 0;
                var input = file.ReadBytes((int)file.Length);
                JxlDecoderSetInput(dec, input, (UIntPtr)input.Length);
                JxlDecoderCloseInput(dec);

                var status = JxlDecoderProcessInput(dec);
                if (status != JxlDec.BasicInfo)
                    return null;

                var info = new JxlBasicInfo();
                if (JxlDec.Success != JxlDecoderGetBasicInfo(dec, ref info))
                    return null;

                return new ImageMetaData
                {
                    Width = info.xsize,
                    Height = info.ysize,
                    BPP = (int)(info.bits_per_sample * (info.num_color_channels + info.num_extra_channels)),
                };
            }
            finally
            {
                JxlDecoderDestroy(dec);
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            IntPtr dec = JxlDecoderCreate(IntPtr.Zero);
            if (dec == IntPtr.Zero)
                throw new ApplicationException("JxlDecoderCreate failed.");
            try
            {
                uint events = (uint)(JxlDec.BasicInfo | JxlDec.ColorEncoding | JxlDec.FullImage);
                if (JxlDec.Success != JxlDecoderSubscribeEvents(dec, events))
                    throw new ApplicationException("JxlDecoderSubscribeEvents failed.");

                file.Position = 0;
                var input = file.ReadBytes((int)file.Length);
                JxlDecoderSetInput(dec, input, (UIntPtr)input.Length);
                JxlDecoderCloseInput(dec);

                var basic_info = new JxlBasicInfo();
                JxlPixelFormat format;
                PixelFormat wpf_format;
                int bpp;

                var status = JxlDecoderProcessInput(dec);
                if (status != JxlDec.BasicInfo)
                    throw new InvalidFormatException();

                if (JxlDec.Success != JxlDecoderGetBasicInfo(dec, ref basic_info))
                    throw new ApplicationException("JxlDecoderGetBasicInfo failed.");

                if (basic_info.alpha_bits > 0)
                {
                    format = new JxlPixelFormat { num_channels = 4, data_type = JxlDataType.Uint8, endianness = JxlEndianness.Native };
                    wpf_format = PixelFormats.Bgra32;
                    bpp = 32;
                }
                else
                {
                    format = new JxlPixelFormat { num_channels = 3, data_type = JxlDataType.Uint8, endianness = JxlEndianness.Native };
                    wpf_format = PixelFormats.Rgb24;
                    bpp = 24;
                }

                UIntPtr buffer_size = UIntPtr.Zero;
                if (JxlDec.Success != JxlDecoderImageOutBufferSize(dec, ref format, ref buffer_size))
                    throw new ApplicationException("JxlDecoderImageOutBufferSize failed.");

                var pixels = new byte[(uint)buffer_size];
                var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    IntPtr bufferPtr = handle.AddrOfPinnedObject();
                    if (JxlDec.Success != JxlDecoderSetImageOutBuffer(dec, ref format, bufferPtr, buffer_size))
                        throw new ApplicationException("JxlDecoderSetImageOutBuffer failed.");

                    status = JxlDecoderProcessInput(dec);
                    while (status != JxlDec.FullImage && status != JxlDec.Error && status != JxlDec.Success)
                    {
                        status = JxlDecoderProcessInput(dec);
                    }

                    if (status != JxlDec.FullImage && status != JxlDec.Success)
                        throw new InvalidFormatException();

                    int stride = (int)info.Width * (bpp / 8);
                    if (wpf_format == PixelFormats.Bgra32) {
                        // RGBA -> BGRA
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte b = pixels[i];
                            pixels[i] = pixels[i + 2];
                            pixels[i + 2] = b;
                        }
                    }
                    var bitmap = BitmapSource.Create((int)info.Width, (int)info.Height, 96, 96, wpf_format, null, pixels, stride);
                    bitmap.Freeze();
                    return new ImageData(bitmap, info);
                }
                finally
                {
                    handle.Free();
                }
            }
            finally
            {
                JxlDecoderDestroy(dec);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("JxlImageFormat.Write not implemented");
        }

        public void Dispose ()
        {
        }
    }
}
