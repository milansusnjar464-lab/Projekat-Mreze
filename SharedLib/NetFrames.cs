using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SharedLib
{
    public static class NetFrames
    {
        public static void SendFrame(Socket s, byte[] payload)
        {
            byte[] len = BitConverter.GetBytes(payload.Length);
            SendAll(s, len);
            SendAll(s, payload);
        }

        public static byte[] RecvFrame(Socket s)
        {
            byte[] lenBytes = RecvExact(s, 4);
            int len = BitConverter.ToInt32(lenBytes, 0);
            if (len < 0 || len > 10_000_000) throw new Exception("Bad frame length");
            return RecvExact(s, len);
        }

        static void SendAll(Socket s, byte[] data)
        {
            int sent = 0;
            while (sent < data.Length)
            {
                int n = s.Send(data, sent, data.Length - sent, SocketFlags.None);
                if (n <= 0) throw new Exception("Send failed");
                sent += n;
            }
        }

        static byte[] RecvExact(Socket s, int count)
        {
            byte[] buf = new byte[count];
            int got = 0;
            while (got < count)
            {
                int n = s.Receive(buf, got, count - got, SocketFlags.None);
                if (n <= 0) throw new Exception("Disconnected");
                got += n;
            }

            return buf;
        }
    }
}
