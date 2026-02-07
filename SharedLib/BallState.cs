using System.IO;
using System.Text;

namespace Shared
{
    public class BallState
    {
        public int X;
        public int Y;

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                bw.Write(X);
                bw.Write(Y);
                bw.Flush();
                return ms.ToArray();
            }
        }

        public static BallState FromBytes(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms, Encoding.UTF8, true))
            {
                return new BallState
                {
                    X = br.ReadInt32(),
                    Y = br.ReadInt32()
                };
            }
        }
    }
}