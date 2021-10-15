using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BasicBuffer
{
    public class RingBufferCollection
    {
        public List<FileStream> Files { get; set; }
        public Dictionary<FileStream, List<RingBuffer>> BuffersByFile = new();
        public Dictionary<RingBuffer, FileStream> BufferFiles = new();
        
        public int Count { get; set; }
        public Dictionary<string, RingBuffer> Buffers = new Dictionary<string, RingBuffer>();
        
        public string Directory { get; set; }

        private Logger Log = LogManager.GetCurrentClassLogger();

        public RingBufferCollection()
        {

        }

        public RingBufferCollection(string path)
        {
            Directory = path;
            var files = System.IO.Directory.GetFiles(path, "*.buf");
            Files = new List<FileStream>();

            for (int i = 0; i < files.Length; i++)
                Files.Add(File.Open(files[i], FileMode.Open, FileAccess.ReadWrite));
        }

        public void LoadBuffers()
        {
            Count = 0;
            
            for (int j = 0; j < Files.Count; j++)
            {
                var Stream = Files[j];
                Stream.Seek(0, SeekOrigin.Begin);
                var currentCount = Stream.ReadInt();
                Count += currentCount;

                for (int i = 0; i < currentCount; i++)
                {
                    string name = Stream.ReadString();
                    RingBuffer buffer = new RingBuffer(Stream, (int) Stream.Position);
                    buffer.Name = name;

                    Stream.Seek(buffer.StreamOffset + buffer.SizeInBytes, SeekOrigin.Begin);

                    Buffers[name] = buffer;
                    if (!BuffersByFile.ContainsKey(Stream))
                        BuffersByFile[Stream] = new List<RingBuffer>();
                    
                    BuffersByFile[Stream].Add(buffer);
                    BufferFiles[buffer] = Stream;
                    
                    Log.Debug("Loaded buffer \"{0}\" of size {1}", name, buffer.Size);
                }
            }

            Log.Debug("Loaded {0} buffers.", Count);
        }

        public int MaximumBufferCountPerFile = 128;

        FileStream GetAvailableFile()
        {
            FileStream nextFile = null;

            foreach (var file in Files)
            {
                if (!BuffersByFile.ContainsKey(file))
                {
                    BuffersByFile[file] = new List<RingBuffer>();
                }

                if (BuffersByFile[file].Count < MaximumBufferCountPerFile)
                {
                    nextFile = file;
                    break;
                }
            }

            if (nextFile == null)
            {
                var nextId = Files.Count;
                var nextName = $"{nextId}.buf";

                while (File.Exists(Path.Combine(Directory, nextName)))
                    nextName = $"{++nextId}.buf";

                nextFile = File.Open(Path.Combine(Directory, nextName), FileMode.Create, FileAccess.ReadWrite);
                
                BuffersByFile[nextFile] = new List<RingBuffer>();
                Files.Add(nextFile);
            }

            return nextFile;
        }
        
        public void AddBuffer(string name, int size)
        {
            AddBuffer(name, RingBuffer.Create(GetAvailableFile(), size));
        }

        public void AddBuffer(string name, RingBuffer buffer)
        {
            var stream = buffer.Stream;
            BuffersByFile[stream].Add(buffer);

            lock(stream)
            {
                stream.Seek(0, SeekOrigin.Begin);
                stream.WriteInt(BuffersByFile[stream].Count);

                stream.Seek(stream.Length, SeekOrigin.Begin);
                stream.WriteString(name);

                buffer.Stream = stream;
                buffer.StreamOffset = (int)stream.Length;
                stream.SetLength(stream.Length + buffer.SizeInBytes);

                buffer.SaveInternal();
                Buffers.Add(name, buffer);
                BufferFiles[buffer] = stream;

                Log.Trace("Added buffer \"{0}\" of size {1}", name, buffer.Size);
            }
        }

        public int Save()
        {
            int saved_total = 0;
            foreach (var file in Files)
            {
                lock (file)
                {
                    foreach (var buffer in BuffersByFile[file])
                    {
                        buffer.SaveInternal();
                        saved_total += buffer.FlushInternal();
                        Log.Debug("Saved buffer \"{0}\"", buffer.Name);
                    }

                }
            }
            
            return saved_total;
        }
    }
}
