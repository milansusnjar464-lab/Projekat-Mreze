using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SharedLib;

class Program
{
    // ====== match thread state ======
    static volatile bool matchRunning = false;
    static Thread matchThread = null;

    static Socket udp = null;
    static EndPoint serverUdp = null;

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

        // ====== TCP connect + login ======
        Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        s.Connect(IPAddress.Parse(ip), 5000);

        SendLine(s, $"LOGIN|{first}|{last}");
        string resp = RecvLine(s);
        Console.WriteLine("Reply: " + resp);

        // ====== INIT (Task 3) - frame receive ======
        // server šalje inicijalne podatke posle prijave
        try
        {
            byte[] initPayload = RecvFrame(s);
            if (initPayload != null && initPayload.Length > 0)
            {
                Igrac igrac;
                Mec mec;
                ParseInitPayload(initPayload, out igrac, out mec);

                Console.WriteLine($"INIT Player: {igrac.Id} {igrac.Ime} {igrac.Prezime} | pobede={igrac.BrojPobeda}, bodovi={igrac.BrojBodova}");
                Console.WriteLine($"INIT Match: p1Y={mec.Igrac1Y}, p2Y={mec.Igrac2Y}, ball=({mec.LopticaX},{mec.LopticaY}), status={(mec.IgraUToku ? "RUNNING" : "STOP")}");
            }
        }
        catch (Exception ex)
        {
            // Ako INIT frame nije uključen ili format ne odgovara, client i dalje može da radi turnir
            Console.WriteLine("INIT read skipped/failed: " + ex.Message);
        }

        Console.WriteLine();
        Console.WriteLine("Waiting for tournament messages (START / END / RANK) ...");

        // ====== TCP receive loop ======
        while (true)
        {
            string line;
            try
            {
                line = RecvLine(s);
            }
            catch
            {
                Console.WriteLine("Disconnected from server.");
                break;
            }

            if (line.StartsWith("START|"))
            {
                StartMatchThread(line, ip);
            }
            else if (line.StartsWith("END|"))
            {
                Console.WriteLine();
                Console.WriteLine("MATCH FINISHED");
                Console.WriteLine(line);

                // stop UDP loop
                matchRunning = false;

                try { matchThread?.Join(500); } catch { }
                try { udp?.Close(); } catch { }

                matchThread = null;
                udp = null;
                serverUdp = null;
            }
            else if (line == "RANK")
            {
                PrintRank(s);
            }
            else
            {
                // ostale poruke (ako ih bude)
                Console.WriteLine(line);
            }
        }

        try { s.Shutdown(SocketShutdown.Both); } catch { }
        try { s.Close(); } catch { }
    }

    // ====== START handling: dobijamo UDP port i pokrećemo background UDP loop ======
    static void StartMatchThread(string line, string serverIp)
    {
        // START|myUdpPort|opponentUdpPort|oppId|oppIme|oppPrezime
        var p = line.Split('|');
        int myPort = int.Parse(p[1]);

        Console.WriteLine();
        Console.WriteLine($"MATCH START -> My UDP port: {myPort} | Opponent: {p[4]} {p[5]}");
        Console.WriteLine("Arrow Up/Down to move. (Match ends automatically on END)");

        // napravi UDP socket za ovaj meč
        udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        udp.Bind(new IPEndPoint(IPAddress.Any, 0)); // lokalni random port
        udp.Blocking = false;

        // šaljemo komande na server port koji je dodeljen ovom igraču
        serverUdp = new IPEndPoint(IPAddress.Parse(serverIp), myPort);

        // startuj thread
        matchRunning = true;

        matchThread = new Thread(MatchLoop);
        matchThread.IsBackground = true;
        matchThread.Start();
    }

    // ====== UDP loop: šalje UP/DOWN i prima Mec stanje ======
    static void MatchLoop()
    {
        byte[] buf = new byte[256];
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);

        while (matchRunning)
        {
            // tastatura (non-blocking)
            if (Console.KeyAvailable)
            {
                var k = Console.ReadKey(true).Key;

                try
                {
                    if (k == ConsoleKey.UpArrow)
                        udp.SendTo(Encoding.UTF8.GetBytes("UP"), serverUdp);
                    else if (k == ConsoleKey.DownArrow)
                        udp.SendTo(Encoding.UTF8.GetBytes("DOWN"), serverUdp);
                }
                catch
                {
                    // server je možda zatvorio port (kraj meča) ili firewall itd.
                }
            }

            // prijem UDP stanja
            try
            {
                int n = udp.ReceiveFrom(buf, ref from);
                if (n > 0)
                {
                    byte[] data = new byte[n];
                    Buffer.BlockCopy(buf, 0, data, 0, n);

                    var mec = Mec.FromBytes(data);

                    Console.SetCursorPosition(0, 12);
                    Console.WriteLine(
                        $"p1Y={mec.Igrac1Y} p2Y={mec.Igrac2Y} ball=({mec.LopticaX},{mec.LopticaY})"
                        .PadRight(70));
                }
            }
            catch (SocketException ex)
            {
                // WouldBlock = nema paketa trenutno
                // ConnectionReset = normalno na Windows UDP kad server port nije dostupan u tom momentu
                if (ex.SocketErrorCode != SocketError.WouldBlock &&
                    ex.SocketErrorCode != SocketError.ConnectionReset)
                {
                    break;
                }
            }

            Thread.Sleep(20);
        }
    }

    // ====== RANK receive: RANK ... RANK_END ======
    static void PrintRank(Socket s)
    {
        Console.WriteLine();
        Console.WriteLine("=== RANK LIST ===");

        while (true)
        {
            string row = RecvLine(s);
            if (row == "RANK_END") break;
            Console.WriteLine(row);
        }

        Console.WriteLine("=================");
    }

    // ====== TCP line helpers ======
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

    // ====== TCP frame helpers (len + payload) ======
    static byte[] RecvFrame(Socket s)
    {
        byte[] lenBytes = RecvExact(s, 4);
        int len = BitConverter.ToInt32(lenBytes, 0);
        if (len < 0 || len > 10_000_000) throw new Exception("Bad frame length: " + len);
        return RecvExact(s, len);
    }

    static byte[] RecvExact(Socket s, int count)
    {
        byte[] buf = new byte[count];
        int off = 0;
        while (off < count)
        {
            int n = s.Receive(buf, off, count - off, SocketFlags.None);
            if (n <= 0) throw new Exception("Disconnected");
            off += n;
        }
        return buf;
    }

    // ====== INIT parsing: [int igrLen][igrBytes][int mecLen][mecBytes] ======
    static void ParseInitPayload(byte[] payload, out Igrac igrac, out Mec mec)
    {
        int pos = 0;

        int igrLen = ReadInt32(payload, ref pos);
        byte[] igrBytes = ReadBytes(payload, ref pos, igrLen);

        int mecLen = ReadInt32(payload, ref pos);
        byte[] mecBytes = ReadBytes(payload, ref pos, mecLen);

        igrac = Igrac.FromBytes(igrBytes);
        mec = Mec.FromBytes(mecBytes);
    }

    static int ReadInt32(byte[] data, ref int pos)
    {
        if (pos + 4 > data.Length) throw new Exception("Bad INIT payload (int32)");
        int v = BitConverter.ToInt32(data, pos);
        pos += 4;
        return v;
    }

    static byte[] ReadBytes(byte[] data, ref int pos, int len)
    {
        if (len < 0 || pos + len > data.Length) throw new Exception("Bad INIT payload (bytes)");
        byte[] b = new byte[len];
        Buffer.BlockCopy(data, pos, b, 0, len);
        pos += len;
        return b;
    }
}