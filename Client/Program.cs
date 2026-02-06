using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

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

        Console.WriteLine("Reply: " + resp);
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
}