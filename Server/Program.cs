using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SharedLib;

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

class Program
{
    static Socket listen;
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
}