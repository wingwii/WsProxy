using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace client
{
    class Server
    {
        private string _wsEndPoint = string.Empty;

        public Server()
        {

        }

        public int Port { get; set; } = 5000;
        public string WsEndPoint
        {
            get { return this._wsEndPoint; }
            set
            {
                var wsEndPoint = value;
                if (wsEndPoint.Equals("@", StringComparison.Ordinal))
                {
                    wsEndPoint = string.Empty;
                }
                else
                {
                    if (!wsEndPoint.EndsWith("/", StringComparison.Ordinal))
                    {
                        wsEndPoint += "/";
                    }
                }
                this._wsEndPoint = wsEndPoint;
            }
        }

        public void Run()
        {
            var task = this.RunAsync();
            task.Wait();
        }

        public async Task RunAsync()
        {
            var listenSock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listenSock.Bind(new IPEndPoint(IPAddress.Any, this.Port));
            listenSock.Listen(8);

            while (true)
            {
                var sock = await listenSock.AcceptAsync();
                _ = this.HandleNewConnection(sock);
            }
        }

        private async Task HandleNewConnection(Socket sock)
        {
            try
            {
                var handler = new ServerHandler(sock, this._wsEndPoint);
                await handler.Run();
            }
            catch (Exception err) 
            {
                err = null;
            }
            try { sock.Close(); }
            catch (Exception) { }
        }

    }
}
