using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Shared;
using SharedLib;

class Program
{
    static void Main()
    {
        Console.Title = "CLIENT";
        Console.OutputEncoding = Encoding.UTF8;

        Console.Write("Server IP (Enter = 127.0.0.1): ");
        string ip = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(ip)) ip = "127.0.0.1";

        Console.Write("First name: ");
        string first = Console.ReadLine();

        Console.Write("Last name: ");
        string last = Console.ReadLine();

        Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        s.Connect(IPAddress.Parse(ip), 5000);

        SendLine(s, $"LOGIN|{first}|{last}");
        string resp = RecvLine(s);

        byte[] initPayload = NetFrames.RecvFrame(s);

        Igrac igrac;
        Mec mec;
        ParseInitPayload(initPayload, out igrac, out mec);

        Console.WriteLine($"INIT Player: {igrac.Id} {igrac.Ime} {igrac.Prezime} | pobede={igrac.BrojPobeda}, bodovi={igrac.BrojBodova}");
        Console.WriteLine($"INIT Match: p1Y={mec.Igrac1Y}, p2Y={mec.Igrac2Y}, ball=({mec.LopticaX},{mec.LopticaY}), status={(mec.IgraUToku ? "RUNNING" : "STOP")}");

        Console.WriteLine("Reply: " + resp);

        Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        udp.Bind(new IPEndPoint(IPAddress.Any, 0));
        udp.Blocking = false;

        EndPoint serverUdp = new IPEndPoint(IPAddress.Parse(ip), 5001);

        Console.WriteLine();
        Console.WriteLine("UDP started. Use Arrow Up/Down. ESC to exit.");

        while (true)
        {
            if (Console.KeyAvailable)
            {
                var k = Console.ReadKey(true).Key;

                if (k == ConsoleKey.UpArrow)
                    udp.SendTo(Encoding.UTF8.GetBytes("UP"), serverUdp);
                else if (k == ConsoleKey.DownArrow)
                    udp.SendTo(Encoding.UTF8.GetBytes("DOWN"), serverUdp);
                else if (k == ConsoleKey.Escape)
                    break;
            }

            byte[] buf = new byte[64];
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);

            try
            {
                int n = udp.ReceiveFrom(buf, ref from);
                if (n > 0)
                {
                    byte[] data = new byte[n];
                    Buffer.BlockCopy(buf, 0, data, 0, n);

                    var st = BallState.FromBytes(data);

                    Console.SetCursorPosition(0, 12);
                    Console.WriteLine($"Ball: ({st.X},{st.Y})".PadRight(50));
                }
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode != SocketError.WouldBlock) throw;
            }

            System.Threading.Thread.Sleep(20);
        }

        try { udp.Close(); } catch { }

        Console.WriteLine("Press ENTER...");
        Console.ReadLine();

        try { s.Shutdown(SocketShutdown.Both); } catch { }
        try { s.Close(); } catch { }
    }

    static void SendLine(Socket s, string msg)
    {
        s.Send(Encoding.UTF8.GetBytes(msg + "\n"));
    }

    static string RecvLine(Socket s)
    {
        var sb = new StringBuilder();
        var b = new byte[1];

        while (true)
        {
            int n = s.Receive(b);
            if (n <= 0) throw new Exception("Disconnected");
            char c = (char)b[0];
            if (c == '\n') break;
            sb.Append(c);
        }

        return sb.ToString().Trim('\r');
    }

    static void ParseInitPayload(byte[] payload, out Igrac igrac, out Mec mec)
    {
        int off = 0;

        int pLen = BitConverter.ToInt32(payload, off);
        off += 4;
        byte[] pBytes = new byte[pLen];
        Buffer.BlockCopy(payload, off, pBytes, 0, pLen);
        off += pLen;

        int mLen = BitConverter.ToInt32(payload, off);
        off += 4;
        byte[] mBytes = new byte[mLen];
        Buffer.BlockCopy(payload, off, mBytes, 0, mLen);

        igrac = Igrac.FromBytes(pBytes);
        mec = Mec.FromBytes(mBytes);
    }
}