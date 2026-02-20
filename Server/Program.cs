using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SharedLib;
using System.Linq;



class TcpPlayer
{
    public Socket Tcp;
    public Igrac Data;
    public bool InMatch;
}

class Conn
{
    public Socket S;
    public StringBuilder Buf = new StringBuilder();
}

class Program
{
    // -------- polje --------
    const int W = 40;   // sirina
    const int H = 20;   // visina
    const int RACKET_H = 4;
    const int POINTS_TO_WIN = 10;

    // -------- TCP --------
    static Socket listen;
    static Dictionary<Socket, Conn> conns = new Dictionary<Socket, Conn>();

    // -------- igraci / turnir --------
    static List<TcpPlayer> tcpPlayers = new List<TcpPlayer>();
    static bool tournamentRunning = false;
    static int nextUdpPort = 6000;
    static Random rng = new Random();

    static void Main()
    {
        Console.Title = "SERVER";
        Console.OutputEncoding = Encoding.UTF8;
        Console.CursorVisible = false;

        listen = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listen.Bind(new IPEndPoint(IPAddress.Any, 5000));
        listen.Listen(20);
        listen.Blocking = false;

        Console.Clear();
        Console.WriteLine("=== PING-PONG SERVER ===");
        Console.WriteLine("TCP slusam na portu 5000");
        Console.WriteLine("Cekam minimum 4 igraca za turnir...");
        Console.WriteLine("Komande: LIST, EXIT");
        Console.WriteLine();

        // -------- glavni loop: ceka igraci i obradjuje komande --------
        while (true)
        {
            if (Console.KeyAvailable)
            {
                string cmd = Console.ReadLine()?.Trim().ToUpperInvariant() ?? "";
                if (cmd == "LIST")
                {
                    Console.WriteLine("--- IGRACI ---");
                    if (tcpPlayers.Count == 0)
                        Console.WriteLine("(niko nije prijavljen)");
                    foreach (var tp in tcpPlayers)
                        Console.WriteLine($"  [{tp.Data.Id}] {tp.Data.Ime} {tp.Data.Prezime}  pobede={tp.Data.BrojPobeda}  bodovi={tp.Data.BrojBodova}");
                    Console.WriteLine();
                }
                else if (cmd == "EXIT")
                {
                    foreach (var s in new List<Socket>(conns.Keys)) DropClient(s);
                    try { listen.Close(); } catch { }
                    Console.WriteLine("Server zatvoren.");
                    return;
                }
            }

            // prihvati nove klijente
            AcceptAll();

            // procitaj podatke od klijenata
            var ready = new List<Socket>(conns.Keys);
            ready.Add(listen);
            try { Socket.Select(ready, null, null, 10_000); } catch { }

            foreach (var s in ready)
            {
                if (s == listen) AcceptAll();
                else ReadFromClient(s);
            }

            // pokreni turnir ako ima dovoljno igraca
            if (!tournamentRunning && tcpPlayers.Count >= 4)
            {
                tournamentRunning = true;
                StartTournament();
            }
        }
    }

    // ====================================================================
    //  TCP ACCEPT / READ
    // ====================================================================

    static void AcceptAll()
    {
        while (true)
        {
            try
            {
                Socket c = listen.Accept();
                c.Blocking = false;
                conns[c] = new Conn { S = c };
                Console.WriteLine($"[KONEKCIJA] {c.RemoteEndPoint}");
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.WouldBlock) break;
            }
        }
    }

    static void ReadFromClient(Socket s)
    {
        byte[] buf = new byte[4096];
        int n;
        try { n = s.Receive(buf); }
        catch (SocketException ex)
        {
            if (ex.SocketErrorCode == SocketError.WouldBlock) return;
            DropClient(s); return;
        }
        if (n <= 0) { DropClient(s); return; }

        var c = conns[s];
        c.Buf.Append(Encoding.UTF8.GetString(buf, 0, n));

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
        // FORMAT: LOGIN|Ime|Prezime
        var p = line.Split('|');
        if (p.Length != 3 || p[0] != "LOGIN")
        {
            TcpSend(s, "ERR|FORMAT");
            return;
        }

        string ime = (p[1] ?? "").Trim();
        string prezime = (p[2] ?? "").Trim();

        if (ime.Length == 0 || prezime.Length == 0)
        {
            TcpSend(s, "ERR|EMPTY");
            return;
        }

        // ID = redni broj
        int newId = tcpPlayers.Count + 1;

        var igrac = new Igrac
        {
            Id = newId,
            Ime = ime,
            Prezime = prezime,
            BrojPobeda = 0,
            BrojBodova = 0
        };

        var tp = new TcpPlayer { Tcp = s, Data = igrac, InMatch = false };
        tcpPlayers.Add(tp);

        Console.WriteLine($"[PRIJAVA] {igrac.Ime} {igrac.Prezime} => ID {igrac.Id}");

        // potvrda prijave
        TcpSend(s, $"OK|{igrac.Id}");

        // Salji inicijalne podatke (Igrac + Mec) kao TCP frame (zadatak 3)
        var mecInit = new Mec
        {
            Igrac1Y = H / 2 - RACKET_H / 2,
            Igrac2Y = H / 2 - RACKET_H / 2,
            LopticaX = W / 2,
            LopticaY = H / 2,
            IgraUToku = false,
            Score1 = 0,
            Score2 = 0
        };

        byte[] iBytes = igrac.ToBytes();
        byte[] mBytes = mecInit.ToBytes();

        // format: [4 bajta len igrac][igrac bajti][4 bajta len mec][mec bajti]
        byte[] payload = new byte[4 + iBytes.Length + 4 + mBytes.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(iBytes.Length), 0, payload, 0, 4);
        Buffer.BlockCopy(iBytes, 0, payload, 4, iBytes.Length);
        int off = 4 + iBytes.Length;
        Buffer.BlockCopy(BitConverter.GetBytes(mBytes.Length), 0, payload, off, 4);
        Buffer.BlockCopy(mBytes, 0, payload, off + 4, mBytes.Length);

        NetFrames.SendFrame(s, payload);

        Console.WriteLine($"  Ukupno prijavljenih: {tcpPlayers.Count}/4");
    }

    static void TcpSend(Socket s, string msg)
    {
        try { s.Send(Encoding.UTF8.GetBytes(msg + "\n")); }
        catch { DropClient(s); }
    }

    static void DropClient(Socket s)
    {
        if (!conns.ContainsKey(s)) return;
        Console.WriteLine($"[ISKLJUCEN] {s.RemoteEndPoint}");
        try { s.Shutdown(SocketShutdown.Both); } catch { }
        try { s.Close(); } catch { }
        conns.Remove(s);
    }

    // ====================================================================
    //  TURNIR (zadatak 5)
    // ====================================================================

    static void StartTournament()
    {
        Console.Clear();
        Console.WriteLine("=== TURNIR POCINAJE ===");

        var lista = tcpPlayers.ToList();
        Shuffle(lista);

        // Generisanje parova (zadatak 5)
        Console.WriteLine("Parovi:");
        for (int i = 0; i + 1 < lista.Count; i += 2)
            Console.WriteLine($"  Mec {i / 2 + 1}: {lista[i].Data.Ime} vs {lista[i + 1].Data.Ime}");
        Console.WriteLine();

        // Obavesti sve igrace o pocetku turnira
        foreach (var tp in tcpPlayers)
            TcpSend(tp.Tcp, "TOURNAMENT_START");

        // Odigraj svaki mec
        int mecBr = 1;
        for (int i = 0; i + 1 < lista.Count; i += 2)
        {
            IgrajiMec(lista[i], lista[i + 1], mecBr++);
            SendRankingToAll();
        }

        Console.WriteLine("=== TURNIR ZAVRSEN ===");
        Console.WriteLine();
        PrintRankingConsole();

        foreach (var tp in tcpPlayers)
            TcpSend(tp.Tcp, "TOURNAMENT_END");

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

    // ====================================================================
    //  MEC (zadatak 4, 5, 6, 7)
    // ====================================================================

    static void IgrajiMec(TcpPlayer p1, TcpPlayer p2, int mecNo)
    {
        // Dodeli dva jedinstvena UDP porta (zadatak 5)
        int port1 = nextUdpPort;
        int port2 = nextUdpPort + 1;
        nextUdpPort += 2;

        p1.InMatch = true;
        p2.InMatch = true;

        // Obavesti igrace: START|mojPort|oppPort|oppId|oppIme|oppPrezime (zadatak 5)
        TcpSend(p1.Tcp, $"START|{port1}|{port2}|{p2.Data.Id}|{p2.Data.Ime}|{p2.Data.Prezime}");
        TcpSend(p2.Tcp, $"START|{port2}|{port1}|{p1.Data.Id}|{p1.Data.Ime}|{p1.Data.Prezime}");

        Console.WriteLine($"\n[MEC {mecNo}] {p1.Data.Ime} (UDP:{port1}) vs {p2.Data.Ime} (UDP:{port2})");

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

            // Pocetne pozicije
            int r1Y = H / 2 - RACKET_H / 2;
            int r2Y = H / 2 - RACKET_H / 2;
            int bx = W / 2;
            int by = H / 2;
            int dx = 1;
            int dy = 1;

            // ---- game loop ----
            while (score1 < POINTS_TO_WIN && score2 < POINTS_TO_WIN)
            {
                // 1. Procitaj komande igraca via UDP (polling model - zadatak 6)
                ReadUdpCommands(udp1, ref ep1, ref r1Y);
                ReadUdpCommands(udp2, ref ep2, ref r2Y);

                // Ogranicenje reketa
                r1Y = Clamp(r1Y, 0, H - RACKET_H);
                r2Y = Clamp(r2Y, 0, H - RACKET_H);

                // 2. Simulacija kretanja loptice (zadatak 6)
                bx += dx;
                by += dy;

                // Odbijanje od gornje i donje ivice
                if (by <= 0) { by = 0; dy = 1; }
                if (by >= H - 1) { by = H - 1; dy = -1; }

                // Odbijanje od levog reketa (igrač 1 je na X=1)
                if (bx <= 1 && by >= r1Y && by < r1Y + RACKET_H)
                {
                    bx = 2;
                    dx = 1;
                    // Ugao odbijanja zavisi od mesta udarca o reket
                    int mid = r1Y + RACKET_H / 2;
                    dy = (by < mid) ? -1 : 1;
                }

                // Odbijanje od desnog reketa (igrač 2 je na X=W-2)
                if (bx >= W - 2 && by >= r2Y && by < r2Y + RACKET_H)
                {
                    bx = W - 3;
                    dx = -1;
                    int mid = r2Y + RACKET_H / 2;
                    dy = (by < mid) ? -1 : 1;
                }

                // Bod
                if (bx <= 0)
                {
                    score2++;
                    ResetBall(ref bx, ref by, ref dx, ref dy);
                }
                else if (bx >= W - 1)
                {
                    score1++;
                    ResetBall(ref bx, ref by, ref dx, ref dy);
                }

                // 3. Pošalji ažurirano stanje oba igraca via UDP (zadatak 6)
                var mec = new Mec
                {
                    Igrac1Y = r1Y,
                    Igrac2Y = r2Y,
                    LopticaX = bx,
                    LopticaY = by,
                    IgraUToku = true,
                    Score1 = score1,
                    Score2 = score2
                };

                byte[] payload = mec.ToBytes();
                if (ep1 != null) try { udp1.SendTo(payload, ep1); } catch { }
                if (ep2 != null) try { udp2.SendTo(payload, ep2); } catch { }

                // 4. Vizualizacija na serveru (zadatak 7)
                RenderMatch(mecNo, p1, p2, mec, score1, score2);

                System.Threading.Thread.Sleep(33); // ~30 FPS
            }
        }

        // Azuriraj statistiku
        int winnerId;
        if (score1 > score2)
        {
            p1.Data.BrojPobeda++;
            winnerId = p1.Data.Id;
        }
        else
        {
            p2.Data.BrojPobeda++;
            winnerId = p2.Data.Id;
        }
        p1.Data.BrojBodova += score1;
        p2.Data.BrojBodova += score2;

        // Obavesti igrace o kraju meca
        TcpSend(p1.Tcp, $"END|{score1}|{score2}|{winnerId}");
        TcpSend(p2.Tcp, $"END|{score2}|{score1}|{winnerId}");

        p1.InMatch = false;
        p2.InMatch = false;

        string winner = (score1 > score2) ? p1.Data.Ime : p2.Data.Ime;
        Console.WriteLine($"\n[MEC {mecNo} KRAJ] {score1}-{score2}  Pobednik: {winner}");
    }

    static void ReadUdpCommands(Socket udp, ref EndPoint ep, ref int racketY)
    {
        byte[] buf = new byte[64];
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
        }
    }

    static void ResetBall(ref int bx, ref int by, ref int dx, ref int dy)
    {
        bx = W / 2;
        by = H / 2;
        dx = (rng.Next(2) == 0) ? -1 : 1;
        dy = (rng.Next(2) == 0) ? -1 : 1;
    }

    static int Clamp(int v, int min, int max)
    {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }

    // ====================================================================
    //  VIZUALIZACIJA - zadatak 7
    //  Pravougaoni teren 20x40, loptica='O', reketi="||" (4 visoka)
    // ====================================================================

    static void RenderMatch(int mecNo, TcpPlayer p1, TcpPlayer p2, Mec mec, int s1, int s2)
    {
        Console.SetCursorPosition(0, 0);
        Console.CursorVisible = false;

        // Zaglavlje
        string header = $"=== MEC {mecNo}: {p1.Data.Ime} [{s1}] vs [{s2}] {p2.Data.Ime} ===";
        Console.WriteLine(header.PadRight(W + 2));
        Console.WriteLine(new string('-', W + 2));

        // Kreiraj matricu polja
        char[,] field = new char[H, W];
        for (int r = 0; r < H; r++)
            for (int c = 0; c < W; c++)
                field[r, c] = ' ';

        // Postavi reketе - zadatak 7: niz od 4 vertikalna '|'
        for (int k = 0; k < RACKET_H; k++)
        {
            int r1 = mec.Igrac1Y + k;
            int r2 = mec.Igrac2Y + k;
            if (r1 >= 0 && r1 < H) { field[r1, 0] = '|'; field[r1, 1] = '|'; }
            if (r2 >= 0 && r2 < H) { field[r2, W - 2] = '|'; field[r2, W - 1] = '|'; }
        }

        // Postavi lopticu - zadatak 7: karakter 'O'
        int bx = Clamp(mec.LopticaX, 0, W - 1);
        int by = Clamp(mec.LopticaY, 0, H - 1);
        field[by, bx] = 'O';

        // Ispis polja sa ivicama
        Console.WriteLine("+" + new string('-', W) + "+");
        for (int r = 0; r < H; r++)
        {
            Console.Write("|");
            for (int c = 0; c < W; c++)
                Console.Write(field[r, c]);
            Console.WriteLine("|");
        }
        Console.WriteLine("+" + new string('-', W) + "+");

        // Rezultat ispod mape - zadatak 7
        Console.WriteLine($"  {p1.Data.Ime}: {s1}  vs  {p2.Data.Ime}: {s2}".PadRight(W + 2));
        Console.WriteLine(new string(' ', W + 2));
    }

    // ====================================================================
    //  RANG LISTA (zadatak 5) - salje se TCP svim igracima nakon svakog meca
    // ====================================================================

    static void SendRankingToAll()
    {
        var sorted = tcpPlayers
            .Select(tp => tp.Data)
            .OrderByDescending(i => i.BrojPobeda)
            .ThenByDescending(i => i.BrojBodova)
            .ToList();

        string header = $"{"ID",-4} {"Ime",-10} {"Prezime",-12} {"Pobede",-8} {"Bodovi",-8}";
        string sep = new string('-', 46);

        foreach (var tp in tcpPlayers)
        {
            TcpSend(tp.Tcp, "RANK");
            TcpSend(tp.Tcp, header);
            TcpSend(tp.Tcp, sep);
            foreach (var ig in sorted)
                TcpSend(tp.Tcp, $"{ig.Id,-4} {ig.Ime,-10} {ig.Prezime,-12} {ig.BrojPobeda,-8} {ig.BrojBodova,-8}");
            TcpSend(tp.Tcp, "RANK_END");
        }
    }

    static void PrintRankingConsole()
    {
        var sorted = tcpPlayers
            .Select(tp => tp.Data)
            .OrderByDescending(i => i.BrojPobeda)
            .ThenByDescending(i => i.BrojBodova)
            .ToList();

        Console.WriteLine($"{"ID",-4} {"Ime",-10} {"Prezime",-12} {"Pobede",-8} {"Bodovi"}");
        Console.WriteLine(new string('-', 50));
        int rank = 1;
        foreach (var ig in sorted)
        {
            Console.WriteLine($"{rank++,2}. [{ig.Id,-2}] {ig.Ime,-10} {ig.Prezime,-12} {ig.BrojPobeda,-8} {ig.BrojBodova}");
        }
        Console.WriteLine();
    }
}
