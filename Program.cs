using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Shared;
using SharedLib;
using System.Linq;

class Player
{
    public int Id;
    public string First;
    public string Last;

    public override string ToString() => $"[{Id}] {First} {Last}";
}

class Conn
{
    public Socket S;
    public StringBuilder Buf = new StringBuilder();
}

class TcpPlayer
{
    public Socket Tcp;
    public Igrac Data;
    public bool InMatch;
}

class Program
{

    static List<TcpPlayer> tcpPlayers = new List<TcpPlayer>();
    static bool tournamentRunning = false;
    static int nextUdpPort = 6000;
    static Random rng = new Random();

    static Socket listen;

    static Socket udp;
    static EndPoint udpClientEp = null;

    static int racketY = 8;
    static int ballX = 20;
    static int ballY = 10;

    static int score = 0;
    static int dirX = 1;

    static Dictionary<Socket, Conn> conns = new Dictionary<Socket, Conn>();
    static List<Player> players = new List<Player>();
    static int nextId = 1;

    static void Main()
    {
        Console.Title = "SERVER";
        Console.OutputEncoding = Encoding.UTF8;

        listen = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listen.Bind(new IPEndPoint(IPAddress.Any, 5000));
        listen.Listen(20);
        listen.Blocking = false;

        udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        udp.Bind(new IPEndPoint(IPAddress.Any, 5001));
        udp.Blocking = false;

        Console.WriteLine("UDP RUNNING on port 5001");

        Console.WriteLine("SERVER RUNNING on port 5000");
        Console.WriteLine("Type LIST or EXIT");
        Console.WriteLine();

        while (true)
        {

            if (Console.KeyAvailable)
            {
                string cmd = Console.ReadLine()?.Trim().ToUpperInvariant();
                if (cmd == "LIST")
                {
                    Console.WriteLine("PLAYERS:");
                    if (players.Count == 0) Console.WriteLine("(none)");
                    foreach (var p in players) Console.WriteLine(p);
                    Console.WriteLine();
                }
                else if (cmd == "EXIT")
                {
                    Shutdown();
                    return;
                }
            }

            HandleUdpInput();
            GameTick();

            var read = new List<Socket> { listen };
            foreach (var s in conns.Keys) read.Add(s);

            Socket.Select(read, null, null, 200_000);

            foreach (var s in read)
            {
                if (s == listen) AcceptOne();
                else ReadFromClient(s);
            }
        }
    }

    static void AcceptOne()
    {
        try
        {
            Socket c = listen.Accept();
            c.Blocking = false;
            conns[c] = new Conn { S = c };
            Console.WriteLine("[CONNECT] " + c.RemoteEndPoint);
        }
        catch (SocketException ex)
        {
            if (ex.SocketErrorCode != SocketError.WouldBlock)
                Console.WriteLine("Accept error: " + ex.SocketErrorCode);
        }
    }

    static void ReadFromClient(Socket s)
    {
        byte[] b = new byte[4096];
        int n;

        try { n = s.Receive(b); }
        catch (SocketException ex)
        {
            if (ex.SocketErrorCode == SocketError.WouldBlock) return;
            Drop(s);
            return;
        }

        if (n <= 0) { Drop(s); return; }

        var c = conns[s];
        c.Buf.Append(Encoding.UTF8.GetString(b, 0, n));

        while (true)
        {
            string all = c.Buf.ToString();
            int idx = all.IndexOf('\n');
            if (idx < 0) break;

            string line = all.Substring(0, idx).Trim('\r', ' ', '\t');
            c.Buf.Remove(0, idx + 1);

            HandleLine(s, line);
        }
    }

    static void HandleLine(Socket s, string line)
    {
        // expected: LOGIN|First|Last
        var p = line.Split('|');
        if (p.Length != 3 || p[0] != "LOGIN")
        {
            SendLine(s, "ERR|FORMAT");
            return;
        }

        string first = (p[1] ?? "").Trim();
        string last = (p[2] ?? "").Trim();

        if (first.Length == 0 || last.Length == 0)
        {
            SendLine(s, "ERR|EMPTY");
            return;
        }

        var pl = new Player { Id = nextId++, First = first, Last = last };
        players.Add(pl);

        Console.WriteLine($"[LOGIN] {pl.First} {pl.Last} => ID {pl.Id}");
        SendLine(s, "OK|" + pl.Id);

        var igrac = new Igrac
        {
            Id = pl.Id,
            Ime = pl.First,
            Prezime = pl.Last,
            BrojPobeda = 0,
            BrojBodova = 0
        };

        tcpPlayers.Add(new TcpPlayer { Tcp = s, Data = igrac, InMatch = false });

        var mec = new Mec
        {
            Igrac1Y = 8,
            Igrac2Y = 8,
            LopticaX = 20,
            LopticaY = 10,
            IgraUToku = false
        };

        byte[] pBytes = igrac.ToBytes();
        byte[] mBytes = mec.ToBytes();

        byte[] initPayload = BuildInitPayload(pBytes, mBytes);
        NetFrames.SendFrame(s, initPayload);

        if (!tournamentRunning && tcpPlayers.Count >= 4)
        {
            tournamentRunning = true;
            Console.WriteLine("=== TOURNAMENT START ===");
            StartTournament();
        }
    }

    static void SendLine(Socket s, string msg)
    {
        try { s.Send(Encoding.UTF8.GetBytes(msg + "\n")); }
        catch { Drop(s); }
    }

    static void Drop(Socket s)
    {
        if (!conns.ContainsKey(s)) return;
        Console.WriteLine("[DISCONNECT] " + s.RemoteEndPoint);
        try { s.Shutdown(SocketShutdown.Both); } catch { }
        try { s.Close(); } catch { }
        conns.Remove(s);
    }

    static void Shutdown()
    {
        foreach (var s in new List<Socket>(conns.Keys)) Drop(s);
        try { listen.Close(); } catch { }
        Console.WriteLine("Server stopped.");
    }


    static byte[] BuildInitPayload(byte[] playerBytes, byte[] matchBytes)
    {
        byte[] pLen = BitConverter.GetBytes(playerBytes.Length);
        byte[] mLen = BitConverter.GetBytes(matchBytes.Length);

        byte[] all = new byte[4 + playerBytes.Length + 4 + matchBytes.Length];

        Buffer.BlockCopy(pLen, 0, all, 0, 4);
        Buffer.BlockCopy(playerBytes, 0, all, 4, playerBytes.Length);

        int off = 4 + playerBytes.Length;
        Buffer.BlockCopy(mLen, 0, all, off, 4);
        Buffer.BlockCopy(matchBytes, 0, all, off + 4, matchBytes.Length);

        return all;
    }

    static void HandleUdpInput()
    {
        byte[] buf = new byte[128];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

        while (true)
        {
            int n;
            try { n = udp.ReceiveFrom(buf, ref remote); }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.WouldBlock) break;
                return;
            }

            if (n <= 0) break;

            udpClientEp = remote;

            string cmd = Encoding.UTF8.GetString(buf, 0, n).Trim();

            if (cmd == "UP") racketY--;
            else if (cmd == "DOWN") racketY++;

            if (racketY < 0) racketY = 0;
            if (racketY > 16) racketY = 16;
        }
    }

    static void GameTick()
    {
        ballX += dirX;

        if (ballX <= 0)
        {
            dirX = 1;
            score++;
        }
        if (ballX >= 39)
        {
            dirX = -1;
        }

        if (udpClientEp != null)
        {
            var st = new BallState { X = ballX, Y = ballY };
            udp.SendTo(st.ToBytes(), udpClientEp);
        }

        RenderServer();
        System.Threading.Thread.Sleep(50);
    }

    static void RenderServer()
    {
        Console.SetCursorPosition(0, 0);
        Console.WriteLine("=== SERVER GAME STATE ===".PadRight(50));
        Console.WriteLine($"RacketY: {racketY}".PadRight(50));
        Console.WriteLine($"Ball: ({ballX},{ballY})".PadRight(50));
        Console.WriteLine($"Score: {score}".PadRight(50));
        Console.WriteLine($"UDP client: {(udpClientEp == null ? "(none)" : udpClientEp.ToString())}".PadRight(50));
        Console.WriteLine("Use client Arrow Up/Down".PadRight(50));
    }

    static void StartTournament()
    {
        var list = tcpPlayers.ToList();
        Shuffle(list);

        for (int i = 0; i + 1 < list.Count; i += 2)
        {
            RunMatch(list[i], list[i + 1], (i / 2) + 1);
            SendRankingToAll();
        }

        Console.WriteLine("=== TOURNAMENT END ===");
        tournamentRunning = false;
    }

    static void Shuffle(List<TcpPlayer> a)
    {
        for (int i = a.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            var t = a[i]; a[i] = a[j]; a[j] = t;
        }
    }


    static void RunMatch(TcpPlayer p1, TcpPlayer p2, int matchNo)
    {
        int port1 = nextUdpPort;
        int port2 = nextUdpPort + 1;
        nextUdpPort += 2;

        p1.InMatch = true;
        p2.InMatch = true;

        // START|myPort|oppPort|oppId|oppIme|oppPrezime
        SendLine(p1.Tcp, $"START|{port1}|{port2}|{p2.Data.Id}|{p2.Data.Ime}|{p2.Data.Prezime}");
        SendLine(p2.Tcp, $"START|{port2}|{port1}|{p1.Data.Id}|{p1.Data.Ime}|{p1.Data.Prezime}");

        Console.WriteLine($"[MATCH {matchNo}] {p1.Data.Ime} vs {p2.Data.Ime} | UDP ports: {port1}/{port2}");

        int score1 = 0, score2 = 0;

        using (Socket udp1 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        using (Socket udp2 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            udp1.Bind(new IPEndPoint(IPAddress.Any, port1));
            udp2.Bind(new IPEndPoint(IPAddress.Any, port2));
            udp1.Blocking = false;
            udp2.Blocking = false;

            EndPoint ep1 = null;
            EndPoint ep2 = null;

            int r1Y = 8, r2Y = 8;
            int bx = 20, by = 10;
            int dx = 1, dy = 1;

            while (score1 < 10 && score2 < 10)
            {
                ReadUdpCmd(udp1, ref ep1, ref r1Y);
                ReadUdpCmd(udp2, ref ep2, ref r2Y);

                bx += dx; by += dy;
                if (by <= 0 || by >= 19) dy = -dy;

                bool hitLeft = (bx == 1 && by >= r1Y && by <= r1Y + 3);
                bool hitRight = (bx == 38 && by >= r2Y && by <= r2Y + 3);

                if (hitLeft) dx = 1;
                if (hitRight) dx = -1;

                if (bx <= 0) { score2++; ResetBall(ref bx, ref by, ref dx, ref dy); }
                if (bx >= 39) { score1++; ResetBall(ref bx, ref by, ref dx, ref dy); }

                var mec = new Mec
                {
                    Igrac1Y = r1Y,
                    Igrac2Y = r2Y,
                    LopticaX = bx,
                    LopticaY = by,
                    IgraUToku = true
                };

                byte[] payload = mec.ToBytes();
                if (ep1 != null) udp1.SendTo(payload, ep1);
                if (ep2 != null) udp2.SendTo(payload, ep2);

                Console.SetCursorPosition(0, 0);
                Console.WriteLine($"MATCH {matchNo}: {p1.Data.Ime} vs {p2.Data.Ime}".PadRight(60));
                Console.WriteLine($"Score: {score1} - {score2}".PadRight(60));
                Console.WriteLine($"Ball=({bx},{by}) R1Y={r1Y} R2Y={r2Y}".PadRight(60));
                Console.WriteLine($"Ports: {port1}/{port2}".PadRight(60));

                System.Threading.Thread.Sleep(30);
            }
        }

        int winnerId = (score1 > score2) ? p1.Data.Id : p2.Data.Id;

        if (score1 > score2) p1.Data.BrojPobeda++;
        else p2.Data.BrojPobeda++;

        p1.Data.BrojBodova += score1;
        p2.Data.BrojBodova += score2;

        SendLine(p1.Tcp, $"END|{score1}|{score2}|{winnerId}");
        SendLine(p2.Tcp, $"END|{score2}|{score1}|{winnerId}");

        p1.InMatch = false;
        p2.InMatch = false;

        Console.WriteLine($"[MATCH END] {score1}-{score2} winner={winnerId}");
    }

    static void ReadUdpCmd(Socket udp, ref EndPoint ep, ref int racketY)
    {
        byte[] buf = new byte[32];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

        while (true)
        {
            int n;
            try { n = udp.ReceiveFrom(buf, ref remote); }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.WouldBlock) break;
                return;
            }

            if (n <= 0) break;

            ep = remote;
            string cmd = Encoding.UTF8.GetString(buf, 0, n).Trim();

            if (cmd == "UP") racketY--;
            else if (cmd == "DOWN") racketY++;

            if (racketY < 0) racketY = 0;
            if (racketY > 16) racketY = 16;
        }
    }

    static void ResetBall(ref int bx, ref int by, ref int dx, ref int dy)
    {
        bx = 20; by = 10;
        dx = (rng.Next(2) == 0) ? -1 : 1;
        dy = (rng.Next(2) == 0) ? -1 : 1;
    }


    static void SendRankingToAll()
    {
        var sorted = tcpPlayers
            .Select(tp => tp.Data)
            .OrderByDescending(i => i.BrojPobeda)
            .ThenByDescending(i => i.BrojBodova)
            .ToList();

        foreach (var tp in tcpPlayers)
        {
            SendLine(tp.Tcp, "RANK");

            SendLine(tp.Tcp, "ID   Ime      Prezime        Pobede  Bodovi");
            SendLine(tp.Tcp, "-------------------------------------------");

            foreach (var p in sorted)
            {
                SendLine(tp.Tcp,
                    $"{p.Id,-4} {p.Ime,-8} {p.Prezime,-12} {p.BrojPobeda,-7} {p.BrojBodova}");
            }

            SendLine(tp.Tcp, "RANK_END");
        }
    }

}