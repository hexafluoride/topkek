using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO;

namespace Heimdall
{
    public class HandshakeData
    {
        public string Name { get; internal set; }
        
        public HandshakeData(byte[] data)
        {
            MemoryStream stream = new MemoryStream(data);
            BinaryReader reader = new BinaryReader(stream);

            Name = reader.BaseStream.ReadString();
        }
    }
}
