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

        public static string ReadString(this Stream stream)
        {
            var len_arr = ByteArrayPool.Rent(4);
            stream.Read(len_arr, 0, 4);
            var str_len = BitConverter.ToInt32(len_arr);
            ByteArrayPool.Return(len_arr);
            
            var str_data = ByteArrayPool.Rent(str_len);
            stream.Read(str_data, 0, str_len);
            var str = Encoding.Unicode.GetString(str_data.AsSpan(0, str_len));
            ByteArrayPool.Return(str_data);

            return str;
        }

        public static Span<byte> WriteInt(this Span<byte> span, int val)
        {
            BitConverter.TryWriteBytes(span, val);
            return span[4..];
        }

        public static Span<byte> WriteBytes(this Span<byte> span, Span<byte> values)
        {
            values.CopyTo(span);
            return span[values.Length..];
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
