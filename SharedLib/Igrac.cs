using System.IO;
using System.Text;

namespace SharedLib
{
    public class Igrac
    {
        public int Id;
        public string Ime;
        public string Prezime;
        public int BrojPobeda;
        public int BrojBodova;

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                bw.Write(Id);
                bw.Write(Ime ?? "");
                bw.Write(Prezime ?? "");
                bw.Write(BrojPobeda);
                bw.Write(BrojBodova);
                bw.Flush();
                return ms.ToArray();
            }
        }

        public static Igrac FromBytes(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms, Encoding.UTF8, true))
            {
                return new Igrac
                {
                    Id = br.ReadInt32(),
                    Ime = br.ReadString(),
                    Prezime = br.ReadString(),
                    BrojPobeda = br.ReadInt32(),
                    BrojBodova = br.ReadInt32()
                };
            }
        }
    }
}