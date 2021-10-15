using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO;
using System.Diagnostics;

namespace Heimdall
{
    public class Message
    {
        public static ArrayPool<byte> Pool = ArrayPool<byte>.Create();
        
        public const int CHECKSUM_SIZE = 0;
        public const uint VERSION = 1;

        public uint Version { get; set; }

        public string Source { get; set; }
        public string Destination { get; set; }

        public string MessageType { get; set; }

        public byte[] Checksum { get; set; }

        public byte[] Data { get; set; }

        public bool Valid { get; internal set; }

        public Message()
        {
            Version = VERSION;
            Data = new byte[0];
        }

        public Message(byte[] data)
            : this()
        {
            Data = data;
            Checksum = Utilities.GetChecksum(Data);
        }

        public Message Clone(bool swap_addr = false)
        {
            Message ret = new Message(Data);

            if (swap_addr)
            {
                ret.Source = this.Destination;
                ret.Destination = this.Source;
            }

            return ret;
        }

        public static Message Consume(Stream stream, bool strict = false)
        {
            try
            {
                Message msg = new Message();
                using (BinaryReader reader = new BinaryReader(stream, Encoding.Unicode, true))
                {
                    msg.Version = reader.ReadUInt32();

                    msg.Source = reader.BaseStream.ReadString();
                    msg.Destination = reader.BaseStream.ReadString();

                    msg.MessageType = reader.BaseStream.ReadString();

                    msg.Checksum = reader.ReadBytes(CHECKSUM_SIZE);

                    int len = reader.ReadInt32();
                    msg.Data = reader.ReadBytes(len);

                    msg.Verify(strict);

                    return msg;
                }
            }
            catch
            {
                return new Message() { Valid = false };
            }
        }

        public static Message Parse(byte[] raw, bool strict = false)
        {
            return Consume(new MemoryStream(raw));   
        }

        public void SerializeTo(Stream stream)
        {
            //Checksum = Utilities.GetChecksum(Data);
            Checksum = new byte[0];

            var total_size = 4 + 4 + 4 + 4 + 4;
            total_size += Checksum.Length;
            total_size += Encoding.Unicode.GetByteCount(Source);
            total_size += Encoding.Unicode.GetByteCount(Destination);
            total_size += Encoding.Unicode.GetByteCount(MessageType);
            total_size += Data.Length;

            var arr = Pool.Rent(total_size);
            arr.AsSpan()
                .WriteInt((int)Version)
                .WriteString(Source)
                .WriteString(Destination)
                .WriteString(MessageType)
                .WriteBytes(Checksum)
                .WriteInt(Data.Length)
                .WriteBytes(Data);

            //return arr.AsSpan()[..total_size];
            stream.Write(arr, 0, total_size);
            Pool.Return(arr);
            return;
/*
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                writer.Write(Version);

                writer.BaseStream.WriteString(Source);
                writer.BaseStream.WriteString(Destination);

                writer.BaseStream.WriteString(MessageType);

                writer.Write(Checksum);

                writer.Write(Data.Length);
                writer.Write(Data);

                writer.Flush();
                writer.Close();

                return ms.ToArray();
            }*/
        }

        internal void Verify(bool strict)
        {
            Valid = true;

            Check(Utilities.GetChecksum(Data).SequenceEqual(Checksum), "Invalid checksum", strict);
            Check(Version == VERSION, "Protocol version mismatch", strict);
        }

        internal void Check(bool condition, string message, bool strict)
        {
            if (strict)
                Debug.Assert(condition, message);
            else
                if (!condition)
                    Valid = false;
        }
    }
}
