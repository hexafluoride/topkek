using NLog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BasicBuffer
{
    public class RingBuffer
    {
        public static readonly int HEADER_LENGTH = 8;
        Logger Log = LogManager.GetCurrentClassLogger();

        public int StreamOffset { get; set; }
        public FileStream Stream { get; set; }

        public int Size { get; set; }
        public int Head { get; set; }
        public string Name { get; set; }

        public int SizeInBytes
        {
            get
            {
                return (Size * BufferElement.ELEMENT_SIZE) + HEADER_LENGTH;
            }
        }

        public int Cached
        {
            get
            {
                return _buffer.Count;
            }
        }

        private Dictionary<int, BufferElement> _buffer;

        private RingBuffer()
        {
        }

        public RingBuffer(FileStream stream, int offset = 0)
        {
            StreamOffset = offset;
            Stream = stream;
            Load();
        }

        public void Load()
        {
            lock (Stream)
            {
                Stream.Seek(StreamOffset, SeekOrigin.Begin);

                Size = Stream.ReadInt();
                Head = Stream.ReadInt();
            }

            _buffer = new Dictionary<int, BufferElement>();
        }

        public void Save()
        {
            lock (Stream)
            {
                SaveInternal();
            }

            Flush();
        }

        internal void SaveInternal()
        {
            Stream.Seek(StreamOffset, SeekOrigin.Begin);

            Stream.WriteInt(Size);
            Stream.WriteInt(Head);
        }

        public static RingBuffer Create(FileStream stream, int size)
        {
            var buffer = new RingBuffer();
            buffer.Stream = stream;
            buffer.Size = size;
            buffer._buffer = new Dictionary<int, BufferElement>();

            //stream.SetLength(size * BufferElement.ELEMENT_SIZE);
            var empty_element = new BufferElement(0, 0);

            for (uint i = 0; i < (uint)size; i++)
            {
                buffer.Write((int)i, empty_element);
            }

            //buffer.Save();
            return buffer;
        }

        public BufferElement Read(int index)
        {
            int absolute_index = GetAbsoluteIndex(index);

            if (_buffer.ContainsKey(absolute_index))
                return _buffer[absolute_index];

            var element = ReadFromFile(absolute_index);
            _buffer[absolute_index] = element;

            return element;
        }

        public void Write(int index, BufferElement element)
        {
            int absolute_index = GetAbsoluteIndex(index);

            _buffer[absolute_index] = element;
            _buffer[absolute_index].Dirty = true;
        }

        public void Write(BufferElement element)
        {
            Write(0, element);
            Head++;

            if (Head >= Size)
                Head = 0;
        }

        public void Flush()
        {
            lock (Stream)
            {
                FlushInternal();
            }
        }

        internal int FlushInternal()
        {
            if (!_buffer.Any())
                return 0;

            int loaded = 0;

            //foreach(var pair in _buffer)
            //{
            //    if (!pair.Value.Dirty)
            //        continue;

            //    WriteToFile(pair.Key);
            //}

            //for(int i = 0; i < _buffer.Count; i++)
            //{
            //    _buffer.Keys
            //}
            for (int i = 0; i < Size; i++)
            {
                if (!_buffer.ContainsKey(i) || !_buffer[i].Dirty)
                    continue;

                WriteToFile(i);
                _buffer[i].Dirty = false;
                loaded++;
            }

            if (_buffer.Count > 100)
            {
                //for (int i = 0; i < Size; i++)
                //{
                //    if(!_buffer.ContainsKey(i))
                //        continue;

                //    _buffer.Remove(i);
                //}

                Log.Info("Cleared buffer");
                _buffer.Clear();
                //_buffer = new Dictionary<int, BufferElement>();
                //Log.Info("Cleared cache");
            }

            Log.Info("Wrote {0} entries", loaded);
            return loaded;
        }

        private int GetAbsoluteIndex(int relative_index)
        {
            if (relative_index >= Size)
                throw new Exception("Invalid index, too big");

            int actual_index = Head + relative_index;

            if (actual_index >= Size)
                actual_index = actual_index - Size;

            return actual_index;
        }

        private void WriteToFile(int real_index)
        {
            int element_size = BufferElement.ELEMENT_SIZE;
            int file_offset = (element_size * real_index) + HEADER_LENGTH + StreamOffset;

            var element = _buffer[real_index];
            
            Stream.Seek(file_offset, SeekOrigin.Begin);
            Stream.Write(element.Serialize(), 0, element_size);
        }

        private BufferElement ReadFromFile(int real_index)
        {
            int element_size = BufferElement.ELEMENT_SIZE;
            int file_offset = (element_size * real_index) + HEADER_LENGTH + StreamOffset;

            byte[] buffer = new byte[8];

            lock (Stream)
            {
                Stream.Seek(file_offset, SeekOrigin.Begin);
                Stream.Read(buffer, 0, element_size);
            }
            
            return BufferElement.Parse(buffer);
        }
    }

    public class BufferElement
    {
        public static readonly int ELEMENT_SIZE = 8;

        public uint Timestamp;
        public float Data;

        public bool Dirty;

        public BufferElement(int time, float data) :
            this((uint)time, data)
        {

        }

        public BufferElement(uint time, float data)
        {
            Timestamp = time;
            Data = data;

            Dirty = false;
        }

        public static BufferElement Parse(byte[] data)
        {
            return new BufferElement(
                BitConverter.ToUInt32(data, 0), 
                BitConverter.ToSingle(data, 4));
        }

        public byte[] Serialize()
        {
            byte[] ret = new byte[8];

            Array.Copy(BitConverter.GetBytes(Timestamp), 0, ret, 0, 4);
            Array.Copy(BitConverter.GetBytes(Data), 0, ret, 4, 4);

            return ret;
        }

        public override string ToString()
        {
            return string.Format("({0}, {1}{2})", Timestamp, Data, Dirty ? ", dirty" : "");
        }
    }
}
