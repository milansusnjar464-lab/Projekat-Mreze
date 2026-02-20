using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SharedLib;



class Program
{
    const int W = 40;
    const int H = 20;
    const int RACKET_H = 4;

    // -------- match state --------
    static volatile bool matchRunning = false;
    static Thread matchThread = null;
    static Socket udp = null;
    static EndPoint serverUdp = null;

    // zadnja primljena stanja (volatile za thread safety)
    static volatile int lastBx = W / 2;
    static volatile int lastBy = H / 2;
    static volatile int lastR1Y = H / 2 - RACKET_H / 2;
    static volatile int lastR2Y = H / 2 - RACKET_H / 2;
    static volatile int lastS1 = 0;
    static volatile int lastS2 = 0;

    // informacije o igracu
    static int myId = 0;
    static string myIme = "";
    static string oppIme = "";
    static bool iAmPlayer1 = false; // da li sam ja igrac1 ili igrac2

    static void Main()
    {
        Console.Title = "KLIJENT";
        Console.OutputEncoding = Encoding.UTF8;
        Console.CursorVisible = false;

        // ---- unos podataka ----
        Console.Clear();
        Console.WriteLine("=== PING-PONG KLIJENT ===");
        Console.Write("Server IP (Enter = 127.0.0.1): ");
        string ip = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";

        Console.Write("Ime: ");
        string ime = Console.ReadLine()?.Trim() ?? "Igrac";
        Console.Write("Prezime: ");
        string prezime = Console.ReadLine()?.Trim() ?? "Nepoznat";
        myIme = ime;

        // ---- TCP konekcija i prijava (zadatak 2) ----
        Socket tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            tcp.Connect(IPAddress.Parse(ip), 5000);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Greska pri konekciji: " + ex.Message);
            Console.ReadKey();
            return;
        }

        // Pošalji LOGIN (zadatak 2)
        TcpSendLine(tcp, $"LOGIN|{ime}|{prezime}");

        // Primi potvrdu
        string resp = TcpRecvLine(tcp);
        if (resp.StartsWith("OK|"))
        {
            int.TryParse(resp.Split('|')[1], out myId);
            Console.WriteLine($"Uspesna prijava! ID = {myId}");
        }
        else
        {
            Console.WriteLine("Greska prijave: " + resp);
            Console.ReadKey();
            return;
        }

        // Primi inicijalne podatke - TCP frame sa Igrac+Mec (zadatak 3)
        try
        {
            byte[] initPayload = TcpRecvFrame(tcp);
            ParseInitPayload(initPayload, out Igrac ig, out Mec mec);
            Console.WriteLine($"Init primljen: ID={ig.Id} {ig.Ime} {ig.Prezime}  pobede={ig.BrojPobeda}  bodovi={ig.BrojBodova}");
            Console.WriteLine($"Mec status: loptica=({mec.LopticaX},{mec.LopticaY})  igra={(mec.IgraUToku ? "u toku" : "stoji")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Init frame greska (nebitno): " + ex.Message);
        }

        Console.WriteLine();
        Console.WriteLine("Cekam pocetak turnira...");

        // ---- TCP receive loop ----
        while (true)
        {
            string line;
            try { line = TcpRecvLine(tcp); }
            catch
            {
                Console.Clear();
                Console.WriteLine("Veza sa serverom prekinuta.");
                break;
            }

            if (string.IsNullOrEmpty(line)) continue;

            if (line == "TOURNAMENT_START")
            {
                Console.Clear();
                Console.WriteLine("=== TURNIR JE POCEO ===");
                Console.WriteLine("Cekaj na raspored meceva...");
            }
            else if (line.StartsWith("START|"))
            {
                HandleStart(line, ip);
            }
            else if (line.StartsWith("END|"))
            {
                HandleEnd(line);
            }
            else if (line == "RANK")
            {
                PrintRank(tcp);
            }
            else if (line == "TOURNAMENT_END")
            {
                Console.WriteLine("\n=== TURNIR ZAVRSEN ===");
                Console.WriteLine("Pritisni Enter za izlaz.");
                Console.ReadLine();
                break;
            }
            else
            {
                Console.WriteLine(line);
            }
        }

        matchRunning = false;
        try { matchThread?.Join(500); } catch { }
        try { tcp.Shutdown(SocketShutdown.Both); } catch { }
        try { tcp.Close(); } catch { }
    }

    // ====================================================================
    //  START: pocni mec, povezi se na UDP, pokreni background nit
    // ====================================================================

    static void HandleStart(string line, string serverIp)
    {
        // START|mojUdpPort|oppUdpPort|oppId|oppIme|oppPrezime
        var p = line.Split('|');
        int myPort = int.Parse(p[1]);
        if (p.Length > 4) oppIme = p[4];

        // Odredi da li sam ja igrac1 ili igrac2 na osnovu porta
        // (p[1] je moj port, p[2] je protivnikov port)
        // igrac1 ima manji port (ili onaj kome je dodeljen port1)
        iAmPlayer1 = (int.Parse(p[1]) < int.Parse(p[2]));

        lastS1 = 0; lastS2 = 0;
        lastBx = W / 2; lastBy = H / 2;
        lastR1Y = H / 2 - RACKET_H / 2;
        lastR2Y = H / 2 - RACKET_H / 2;

        Console.Clear();
        Console.WriteLine($"=== MEC: {myIme} vs {oppIme} ===");
        Console.WriteLine($"Moj UDP port na serveru: {myPort}");
        Console.WriteLine("Strelice GORE/DOLE za kretanje reketa");
        Console.WriteLine();

        // UDP socket
        udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        udp.Bind(new IPEndPoint(IPAddress.Any, 0)); // lokalni random port
        udp.Blocking = false;

        serverUdp = new IPEndPoint(IPAddress.Parse(serverIp), myPort);

        matchRunning = true;
        matchThread = new Thread(MatchLoop);
        matchThread.IsBackground = true;
        matchThread.Start();
    }

    // ====================================================================
    //  MATCH LOOP - polling model (zadatak 6)
    //  Klijent poluje: cita UDP stanje, salje komande, renderuje
    // ====================================================================

    static void MatchLoop()
    {
        byte[] buf = new byte[256];
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);

        while (matchRunning)
        {
            // --- 1. Procitaj input tastature i pošalji komandu UDP-om ---
            // (Console.KeyAvailable - ne blokira, zadatak 4)
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
                catch { }
            }

            // --- 2. Poluj UDP za novo stanje igre (polling model - zadatak 6) ---
            try
            {
                int n = udp.ReceiveFrom(buf, ref from);
                if (n > 0)
                {
                    byte[] data = new byte[n];
                    Buffer.BlockCopy(buf, 0, data, 0, n);

                    var mec = Mec.FromBytes(data);

                    // Azuriraj lokalni state
                    lastBx = mec.LopticaX;
                    lastBy = mec.LopticaY;
                    lastR1Y = mec.Igrac1Y;
                    lastR2Y = mec.Igrac2Y;
                    lastS1 = mec.Score1;
                    lastS2 = mec.Score2;

                    // --- 3. Vizualizacija (zadatak 7) ---
                    RenderGame();
                }
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode != SocketError.WouldBlock &&
                    ex.SocketErrorCode != SocketError.ConnectionReset)
                    break;
            }

            Thread.Sleep(16); // ~60 Hz polling
        }
    }

    static void HandleEnd(string line)
    {
        matchRunning = false;
        try { matchThread?.Join(1000); } catch { }
        try { udp?.Close(); } catch { }
        udp = null;
        matchThread = null;
        serverUdp = null;

        // END|mojScore|oppScore|winnerId
        var p = line.Split('|');
        int myScore = p.Length > 1 ? int.Parse(p[1]) : 0;
        int oppScore = p.Length > 2 ? int.Parse(p[2]) : 0;
        int winnerId = p.Length > 3 ? int.Parse(p[3]) : 0;

        Console.Clear();
        Console.WriteLine("=== MEC ZAVRSEN ===");
        Console.WriteLine($"Rezultat: {myIme} {myScore} - {oppScore} {oppIme}");
        if (winnerId == myId)
            Console.WriteLine("POBEDIO SI! :)");
        else
            Console.WriteLine("Izgubio si. :(");
        Console.WriteLine();
        Console.WriteLine("Cekam rang listu...");
    }

    // ====================================================================
    //  VIZUALIZACIJA - zadatak 7
    //  20x40 polje, loptica='O', reketi="||" (4 visoka)
    //  Rezultat iznad mape
    // ====================================================================

    static void RenderGame()
    {
        Console.SetCursorPosition(0, 0);
        Console.CursorVisible = false;

        // Rezultat iznad mape (zadatak 7)
        string scoreStr;
        if (iAmPlayer1)
            scoreStr = $"  {myIme} [{lastS1}] vs [{lastS2}] {oppIme}";
        else
            scoreStr = $"  {oppIme} [{lastS1}] vs [{lastS2}] {myIme}";

        Console.WriteLine(scoreStr.PadRight(W + 4));
        Console.WriteLine(("  Strelice GORE/DOLE za kretanje").PadRight(W + 4));
        Console.WriteLine(("  +" + new string('-', W) + "+").PadRight(W + 4));

        // Kreiraj polje
        char[,] field = new char[H, W];
        for (int r = 0; r < H; r++)
            for (int c = 0; c < W; c++)
                field[r, c] = ' ';

        // Reketi - zadatak 7: niz 4 vertikalna '|'
        for (int k = 0; k < RACKET_H; k++)
        {
            int r1 = lastR1Y + k;
            int r2 = lastR2Y + k;
            if (r1 >= 0 && r1 < H) { field[r1, 0] = '|'; field[r1, 1] = '|'; }
            if (r2 >= 0 && r2 < H) { field[r2, W - 2] = '|'; field[r2, W - 1] = '|'; }
        }

        // Loptica - zadatak 7: 'O'
        int bx = Clamp(lastBx, 0, W - 1);
        int by = Clamp(lastBy, 0, H - 1);
        field[by, bx] = 'O';

        // Ispis
        var sb = new StringBuilder();
        for (int r = 0; r < H; r++)
        {
            sb.Append("  |");
            for (int c = 0; c < W; c++)
                sb.Append(field[r, c]);
            sb.AppendLine("|");
        }
        Console.Write(sb.ToString());
        Console.WriteLine(("  +" + new string('-', W) + "+").PadRight(W + 4));

        // Prikaz mog reketa
        string myRacketInfo;
        if (iAmPlayer1)
            myRacketInfo = $"  Moj reket Y: {lastR1Y}  |  Protivnik Y: {lastR2Y}";
        else
            myRacketInfo = $"  Moj reket Y: {lastR2Y}  |  Protivnik Y: {lastR1Y}";

        Console.WriteLine(myRacketInfo.PadRight(W + 4));
        Console.WriteLine(($"  Loptica: ({lastBx},{lastBy})").PadRight(W + 4));
    }

    static int Clamp(int v, int min, int max)
    {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }

    // ====================================================================
    //  RANG LISTA (zadatak 5)
    // ====================================================================

    static void PrintRank(Socket tcp)
    {
        Console.Clear();
        Console.WriteLine("=== RANG LISTA ===");

        while (true)
        {
            string row;
            try { row = TcpRecvLine(tcp); }
            catch { break; }

            if (row == "RANK_END") break;
            Console.WriteLine(row);
        }
        Console.WriteLine("==================");
        Console.WriteLine();
    }

    // ====================================================================
    //  TCP HELPERS
    // ====================================================================

    static void TcpSendLine(Socket s, string msg)
    {
        s.Send(Encoding.UTF8.GetBytes(msg + "\n"));
    }

    static string TcpRecvLine(Socket s)
    {
        var sb = new StringBuilder();
        var b = new byte[1];
        while (true)
        {
            int n = s.Receive(b);
            if (n <= 0) throw new Exception("Veza prekinuta");
            char c = (char)b[0];
            if (c == '\n') break;
            sb.Append(c);
        }
        return sb.ToString().TrimEnd('\r');
    }

    static byte[] TcpRecvFrame(Socket s)
    {
        byte[] lenB = TcpRecvExact(s, 4);
        int len = BitConverter.ToInt32(lenB, 0);
        if (len < 0 || len > 10_000_000) throw new Exception("Lose frame duzine: " + len);
        return TcpRecvExact(s, len);
    }

    static byte[] TcpRecvExact(Socket s, int count)
    {
        byte[] buf = new byte[count];
        int off = 0;
        while (off < count)
        {
            int n = s.Receive(buf, off, count - off, SocketFlags.None);
            if (n <= 0) throw new Exception("Veza prekinuta");
            off += n;
        }
        return buf;
    }

    // ====================================================================
    //  INIT PAYLOAD PARSER (zadatak 3)
    // ====================================================================

    static void ParseInitPayload(byte[] payload, out Igrac igrac, out Mec mec)
    {
        int pos = 0;
        int iLen = ReadInt32LE(payload, ref pos);
        byte[] iB = ReadBytes(payload, ref pos, iLen);
        int mLen = ReadInt32LE(payload, ref pos);
        byte[] mB = ReadBytes(payload, ref pos, mLen);
        igrac = Igrac.FromBytes(iB);
        mec = Mec.FromBytes(mB);
    }

    static int ReadInt32LE(byte[] d, ref int pos)
    {
        int v = BitConverter.ToInt32(d, pos);
        pos += 4;
        return v;
    }

    static byte[] ReadBytes(byte[] d, ref int pos, int len)
    {
        byte[] b = new byte[len];
        Buffer.BlockCopy(d, pos, b, 0, len);
        pos += len;
        return b;
    }
}
