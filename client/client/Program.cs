using System;

namespace client
{
    class Program
    {
        static void Main(string[] args)
        {
            var server = new Server();
            server.Port = int.Parse(args[0]);
            server.WsEndPoint = args[1];
            server.Run();
        }
    }
}
