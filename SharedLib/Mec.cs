using System.IO;
using System.Text;

namespace SharedLib
{
    public class Mec
    {
        public int Igrac1Y;
        public int Igrac2Y;
        public int LopticaX;
        public int LopticaY;
        public bool IgraUToku;

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                bw.Write(Igrac1Y);
                bw.Write(Igrac2Y);
                bw.Write(LopticaX);
                bw.Write(LopticaY);
                bw.Write(IgraUToku);
                bw.Flush();
                return ms.ToArray();
            }
        }

        public static Mec FromBytes(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms, Encoding.UTF8, true))
            {
                return new Mec
                {
                    Igrac1Y = br.ReadInt32(),
                    Igrac2Y = br.ReadInt32(),
                    LopticaX = br.ReadInt32(),
                    LopticaY = br.ReadInt32(),
                    IgraUToku = br.ReadBoolean()
                };
            }
        }
    }
}