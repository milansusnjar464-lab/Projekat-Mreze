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
        public int Score1;  // Dodato za zadatak 7
        public int Score2;  // Dodato za zadatak 7

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
                bw.Write(Score1);
                bw.Write(Score2);
                bw.Flush();
                return ms.ToArray();
            }
        }

        public static Mec FromBytes(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms, Encoding.UTF8, true))
            {
                var mec = new Mec
                {
                    Igrac1Y = br.ReadInt32(),
                    Igrac2Y = br.ReadInt32(),
                    LopticaX = br.ReadInt32(),
                    LopticaY = br.ReadInt32(),
                    IgraUToku = br.ReadBoolean()
                };

                // Provera da li ima još podataka (za kompatibilnost sa starim verzijama)
                if (ms.Position < ms.Length)
                {
                    mec.Score1 = br.ReadInt32();
                    mec.Score2 = br.ReadInt32();
                }

                return mec;
            }
        }
    }
}