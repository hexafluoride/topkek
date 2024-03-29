﻿using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;

namespace Backyard
{
    public class FastBitmap
    {
        public Bitmap InternalBitmap;
        private BitmapData Handle;

        private Random Random = new Random();

        public byte[] Data;
        public bool Locked { get; set; }

        public int Width
        {
            get
            {
                return _width;
            }
        }

        public int Height
        {
            get
            {
                return _height;
            }
        }

        public int Subwidth
        {
            get
            {
                return Handle.Stride;
            }
        }

        // .NET Bitmap.Width and Height aren't thread-safe

        private int _width = 0;
        private int _height = 0;

        // .NET Bitmap.PixelFormat isn't thread-safe
        private PixelFormat Format;

        public FastBitmap(Bitmap internal_bitmap)
        {
            InternalBitmap = internal_bitmap;
            Format = InternalBitmap.PixelFormat;
            _width = InternalBitmap.Width;
            _height = InternalBitmap.Height;
        }


        public FastBitmap(string path) :
            this(new Bitmap(path))
        {
        }

        public FastBitmap(int width, int height, PixelFormat format = PixelFormat.Format32bppArgb)
        {
            InternalBitmap = new Bitmap(width, height, format);
            Format = format;
            _width = width;
            _height = height;
        }

        public void Save(string path)
        {
            Unlock();
            InternalBitmap.Save(path, ImageFormat.Png);
        }

        public void Lock()
        {
            if (Locked)
                return;

            Handle = InternalBitmap.LockBits(
                new Rectangle(Point.Empty, InternalBitmap.Size),
                ImageLockMode.ReadWrite,
                Format);

            Data = new byte[Handle.Stride * InternalBitmap.Height];
            Marshal.Copy(Handle.Scan0, Data, 0, Data.Length);

            Locked = true;
        }

        public void Unlock(bool apply = true)
        {
            if (!Locked)
                return;

            if (apply)
                Marshal.Copy(Data, 0, Handle.Scan0, Data.Length);

            InternalBitmap.UnlockBits(Handle);

            Locked = false;
        }

        public void Dispose()
        {
            if (Locked)
            {
                Unlock();
            }

            InternalBitmap.Dispose();
        }

        public Bitmap GetSnapshot()
        {
            if (Data == null || Data.Length == 0)
                throw new Exception("Buffer is empty");

            Bitmap bmp = new Bitmap(InternalBitmap);

            var handle = bmp.LockBits(
                 new Rectangle(Point.Empty, bmp.Size),
                 ImageLockMode.WriteOnly,
                 bmp.PixelFormat);

            Marshal.Copy(Data, 0, handle.Scan0, Data.Length);

            bmp.UnlockBits(handle);

            return bmp;
        }

        public FastBitmap GetFastSnapshot()
        {
            if (Data == null || Data.Length == 0)
                throw new Exception("Buffer is empty");

            FastBitmap bmp = new FastBitmap(Width, Height, Format);
            bmp.Lock();

            Array.Copy(Data, bmp.Data, Data.Length);

            return bmp;
        }

        public Color GetPixel(int x, int y)
        {
            switch (Format)
            {
                case PixelFormat.Format16bppArgb1555:
                    {
                        int index = (y * Handle.Stride) + (x * 2); // 16bpp, 2 bytes

                        byte first_half = Data[index]; // contains 1 alpha bit + 5 bits of red + 2 bits of green
                        byte second_half = Data[index + 1]; // contains 3 bits of green + 5 bits of blue

                        return Color.FromArgb(
                            (first_half & 0x80) == 0x80 ? 255 : 0,
                            (first_half & 0x7C) << 3,
                            (((first_half & 0x03) << 3) + ((second_half & 0xE0) >> 5)) << 3,
                            (second_half & 0x1F) << 3);
                    }
                case PixelFormat.Format16bppGrayScale:
                    {
                        int index = (y * Handle.Stride) + (x * 2); // 16bpp, 2 bytes

                        byte first_half = Data[index];

                        return Color.FromArgb(first_half, first_half, first_half);
                    }
                case PixelFormat.Format16bppRgb555:
                    {
                        int index = (y * Handle.Stride) + (x * 2); // 16bpp, 2 bytes

                        byte first_half = Data[index]; // contains 1 unused bit + 5 bits of red + 2 bits of green
                        byte second_half = Data[index + 1]; // contains 3 bits of green + 5 bits of blue

                        return Color.FromArgb(
                            (first_half & 0x7C) << 3,
                            (((first_half & 0x03) << 3) + ((second_half & 0xE0) >> 5)) << 3,
                            (second_half & 0x1F) << 3);
                    }
                case PixelFormat.Format16bppRgb565:
                    {
                        int index = (y * Handle.Stride) + (x * 2); // 16bpp, 2 bytes

                        byte first_half = Data[index]; // contains 5 bits of red + 3 bits of green
                        byte second_half = Data[index + 1]; // contains 3 bits of green + 5 bits of blue

                        return Color.FromArgb(
                            (first_half & 0xF8) << 3,
                            (((first_half & 0x07) << 3) + ((second_half & 0xE0) >> 5)) << 2,
                            (second_half & 0x1F) << 3);
                    }
                case PixelFormat.Format24bppRgb:
                    {
                        int index = (y * Handle.Stride) + (x * 3); // 24bpp, 3 bytes

                        return Color.FromArgb(
                            Data[index + 2],
                            Data[index + 1],
                            Data[index]);
                    }
                case PixelFormat.Format32bppArgb:
                    {
                        int index = (y * Handle.Stride) + (x * 4); // 32bpp, 4 bytes

                        return Color.FromArgb(
                            Data[index + 3],
                            Data[index + 2],
                            Data[index + 1],
                            Data[index]);
                    }
                default:
                    throw new Exception("Unsupported pixel format " + Format);
            }
        }

        public void SetPixel(int x, int y, Color clr)
        {
            switch (Format)
            {
                case PixelFormat.Format24bppRgb:
                    {
                        int index = (y * Handle.Stride) + (x * 3); // 24bpp, 3 bytes

                        Data[index] = clr.B;
                        Data[index + 1] = clr.G;
                        Data[index + 2] = clr.R;

                        break;
                    }
                case PixelFormat.Format32bppArgb:
                    {
                        int index = (y * Handle.Stride) + (x * 4); // 32bpp, 4 bytes

                        Data[index] = clr.B;
                        Data[index + 1] = clr.G;
                        Data[index + 2] = clr.R;
                        Data[index + 3] = clr.A;

                        break;
                    }
                default:
                    throw new Exception("Unsupported pixel format " + Format);
            }
        }

        public byte[] GetRawBytes(int x, int y)
        {
            int index = (y * Handle.Stride);
            int len = 0;

            switch (Format)
            {
                case PixelFormat.Format16bppArgb1555:
                case PixelFormat.Format16bppGrayScale:
                case PixelFormat.Format16bppRgb555:
                case PixelFormat.Format16bppRgb565:
                    len = 2;
                    break;
                case PixelFormat.Format24bppRgb:
                    len = 3;
                    break;
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb:
                case PixelFormat.Format32bppRgb:
                    len = 4;
                    break;
                case PixelFormat.Format48bppRgb:
                    len = 6;
                    break;
                case PixelFormat.Format64bppPArgb:
                    len = 8;
                    break;
                default:
                    throw new Exception("Unsupported pixel format " + Format);
            }

            index += x * len;

            byte[] ret = new byte[len];
            Array.Copy(Data, index, ret, 0, len);

            return ret;
        }

        public Dictionary<string, byte[]> SeparateChannels()
        {
            Dictionary<string, byte[]> ret = new Dictionary<string, byte[]>();

            switch (Format)
            {
                case PixelFormat.Format24bppRgb:
                    {
                        byte[] r = new byte[Data.Length / 3];
                        byte[] g = new byte[Data.Length / 3];
                        byte[] b = new byte[Data.Length / 3];

                        for (int y = 0; y < Height; y++)
                        {
                            for (int x = 0; x < Width; x++)
                            {
                                int index = (y * Width) + x;
                                byte[] raw = GetRawBytes(x, y);

                                b[index] = raw[0];
                                g[index] = raw[1];
                                r[index] = raw[2];
                            }
                        }

                        ret.Add("R", r);
                        ret.Add("G", g);
                        ret.Add("B", b);

                        return ret;
                    }
                case PixelFormat.Format32bppArgb:
                    {
                        byte[] a = new byte[Data.Length / 4];
                        byte[] r = new byte[Data.Length / 4];
                        byte[] g = new byte[Data.Length / 4];
                        byte[] b = new byte[Data.Length / 4];

                        for (int y = 0; y < Height; y++)
                        {
                            for (int x = 0; x < Width; x++)
                            {
                                int index = (y * Width) + x;
                                byte[] raw = GetRawBytes(x, y);

                                b[index] = raw[0];
                                g[index] = raw[1];
                                r[index] = raw[2];
                                a[index] = raw[3];
                            }
                        }

                        ret.Add("R", r);
                        ret.Add("G", g);
                        ret.Add("B", b);
                        ret.Add("A", a);

                        return ret;
                    }
                default:
                    throw new Exception("Unsupported pixel format " + Format);
            }
        }

        public void WriteChannels(Dictionary<string, byte[]> channels)
        {
            switch (Format)
            {
                case PixelFormat.Format24bppRgb:
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            string id = "";

                            switch (c)
                            {
                                case 0:
                                    id = "B";
                                    break;
                                case 1:
                                    id = "G";
                                    break;
                                case 2:
                                    id = "R";
                                    break;
                            }

                            if (!channels.ContainsKey(id))
                                continue;

                            var channel = channels[id];

                            for (int i = 0; i < channel.Length; i++)
                            {
                                Data[(i * 3) + c] = channel[i];
                            }
                        }

                        break;
                    }
                case PixelFormat.Format32bppArgb:
                    {
                        for (int c = 0; c < 4; c++)
                        {
                            string id = "";

                            switch (c)
                            {
                                case 0:
                                    id = "B";
                                    break;
                                case 1:
                                    id = "G";
                                    break;
                                case 2:
                                    id = "R";
                                    break;
                                case 3:
                                    id = "A";
                                    break;
                            }

                            if (!channels.ContainsKey(id))
                                continue;

                            var channel = channels[id];

                            for (int i = 0; i < channel.Length; i++)
                            {
                                Data[(i * 4) + c] = channel[i];
                            }
                        }

                        break;
                    }
                default:
                    throw new Exception("Unsupported pixel format " + Format);
            }
        }
    }

    public static class ColorHelpers
    {
        public static Channel FindRecessive(Color color)
        {
            if (color.R < color.G && color.R < color.B)
                return Channel.R;
            else if (color.G < color.R && color.G < color.B)
                return Channel.G;
            else if (color.B < color.R && color.B < color.G)
                return Channel.B;

            return Channel.R;
        }

        public static int GetValue(Color color, Channel channel)
        {
            if (channel == Channel.R)
                return color.R;
            if (channel == Channel.G)
                return color.G;
            if (channel == Channel.B)
                return color.B;

            return 0;
        }

        public static Color ColorFromHSV(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - saturation));
            int q = Convert.ToInt32(value * (1 - f * saturation));
            int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

            int a = 8;

            if (hi == 0)
                return Color.FromArgb(a, v, t, p);
            else if (hi == 1)
                return Color.FromArgb(a, q, v, p);
            else if (hi == 2)
                return Color.FromArgb(a, p, v, t);
            else if (hi == 3)
                return Color.FromArgb(a, p, q, v);
            else if (hi == 4)
                return Color.FromArgb(a, t, p, v);
            else
                return Color.FromArgb(a, v, p, q);
        }

        public static Color Subtract(Color first, Color second)
        {
            return SafeRgb(
                first.R - second.R,
                first.G - second.G,
                first.B - second.B);
        }

        public static Color Blend(Color background, Color foreground)
        {
            double a = foreground.A / 255.0;

            return SafeRgb(
                (int)((foreground.R * a) + (background.R * (1 - a))),
                (int)((foreground.G * a) + (background.G * (1 - a))),
                (int)((foreground.B * a) + (background.B * (1 - a))));
        }

        public static Color Add(Color first, Color second)
        {
            return SafeRgb(
                first.R + second.R,
                first.G + second.G,
                first.B + second.B);
        }

        public static Color OverflowingAdd(Color first, Color second)
        {
            byte r = (byte)(first.R + second.R);
            byte g = (byte)(first.G + second.G);
            byte b = (byte)(first.B + second.B);

            return Color.FromArgb(r, g, b);
        }

        public static Color Delta(Color first, Color second)
        {
            byte r = (byte)(first.R - second.R);
            byte g = (byte)(first.G - second.G);
            byte b = (byte)(first.B - second.B);

            return Color.FromArgb(r, g, b);
        }

        public static Color SafeRgb(int r, int g, int b, int a = 255)
        {
            a = Math.Min(Math.Max(0, a), 255);
            r = Math.Min(Math.Max(0, r), 255);
            g = Math.Min(Math.Max(0, g), 255);
            b = Math.Min(Math.Max(0, b), 255);

            return Color.FromArgb(a, r, g, b);
        }

        public static byte MinDelta(int x, int y, int z, int t)
        {
            return (byte)Min(
                Math.Abs((byte)(x - t)),
                Math.Abs((byte)(y - t)),
                Math.Abs((byte)(z - t)));
        }

        public static int Min(int x, int y, int z)
        {
            return Math.Min(x, Math.Min(y, z));
        }
    }

    public enum Channel
    {
        R, G, B
    }
}

