using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO;
using System.Security.Cryptography;

namespace Heimdall
{
    public static class Utilities
    {
        public static ArrayPool<byte> ByteArrayPool = ArrayPool<byte>.Shared; 
    
        public static byte[] GetChecksum(byte[] data)
        {
            return new byte[0];
            SHA256CryptoServiceProvider sha = new SHA256CryptoServiceProvider();
            return sha.ComputeHash(data);
        }

        public static void ReadString(this Stream stream, ref Span<char> str)
        {
            // if (str.Length != len)
            // {
            //     var strBuf = ByteArrayPool.Rent(len);
            //     stream.Read(strBuf, 0, len);
            //     str = Encoding.Unicode.GetString(strBuf);
            // }
            // else
            // {
            //     Encoding.Unicode.GetDecoder().
            // }
            
            // var str_data = ByteArrayPool.Rent(str_len);
            // stream.Read(str_data, 0, str_len);
            // //var str = Encoding.Unicode.GetString(str_data.AsSpan(0, str_len));
            // ByteArrayPool.Return(str_data);
        }

        public static Span<byte> ReadBytesCertain(this Stream stream, Span<byte> span)
        {
            var tempSpan = span;
            while (tempSpan.Length > 0)
                tempSpan = tempSpan.Slice(stream.Read(tempSpan));
            return tempSpan;
        }
        
        public static string ReadString(this Stream stream)
        {
            Span<byte> lenBytes = stackalloc byte[4];
            stream.ReadBytesCertain(lenBytes);
            var len = BitConverter.ToInt32(lenBytes);

            var str_data = ByteArrayPool.Rent(len);
            stream.Read(str_data, 0, len);
            var str = Encoding.Unicode.GetString(str_data.AsSpan(0, len));
            ByteArrayPool.Return(str_data);

            return str;
        }

        public static Span<byte> WriteInt(this Span<byte> span, int val)
        {
            BitConverter.TryWriteBytes(span, val);
            return span[4..];
        }

        public static Span<byte> WriteBytes(this Span<byte> span, ReadOnlySpan<byte> values)
        {
            values.CopyTo(span);
            return span[values.Length..];
        }

        public static ReadOnlySpan<byte> ReadString(this ReadOnlySpan<byte> span, out string str)
        {
            var strBytes = BitConverter.ToInt32(span);
            str = Encoding.Unicode.GetString(span.Slice(4, strBytes));
            return span.Slice(4 + strBytes);
        }

        public static Span<byte> Pack(IEnumerable<string> strings, Span<byte> buffer)
        {
            foreach (var str in strings)
            {
                buffer = buffer.WriteString(str);
            }

            return buffer;
        }

        public static Span<byte> WriteString(this Span<byte> span, string str)
        {
            var arr_len = Encoding.Unicode.GetByteCount(str);
            BitConverter.TryWriteBytes(span, arr_len);
            Encoding.Unicode.GetBytes(str, span[4..]);
            return span[(arr_len + 4)..];
        }
        
        public static void WriteString(this Stream stream, string str)
        {
            var arr_len = Encoding.Unicode.GetByteCount(str);
            var str_data = ByteArrayPool.Rent(arr_len + 4);

            BitConverter.TryWriteBytes(str_data, arr_len);
            Encoding.Unicode.GetBytes(str, str_data.AsSpan(4));
            stream.Write(str_data, 0, arr_len + 4);

            ByteArrayPool.Return(str_data);
        }
    }
}
