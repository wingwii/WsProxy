using System;

namespace client
{
    class Program
    {
        static void Main(string[] args)
        {
            var argc = args.Length;

            var server = new Server();
            server.Port = int.Parse(args[0]);
            server.WsEndPoint = args[1];

            if (argc > 2)
            {
                server.ForwarderEndPoint = args[2];
            }

            server.Run();
        }
    }
}
