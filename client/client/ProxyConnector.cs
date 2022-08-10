using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace client
{
    class ProxyConnector
    {
        private string _wsEndPoint = string.Empty;
        private string _dstHost = string.Empty;
        private int _dstPort = 0;
        private Socket _sock = null;
        private ClientWebSocket _ws = null;

        public ProxyConnector(string wsEndPoint)
        {
            this._wsEndPoint = wsEndPoint;
        }

        public override string ToString()
        {
            return (this._dstHost + ":" + this._dstPort.ToString());
        }

        public void Shutdown()
        {
            if (this._sock != null)
            {
                try { this._sock.Shutdown(SocketShutdown.Both); }
                catch (Exception) { }
            }

            try
            {
                var buf = new byte[4];
                var arSeg = new ArraySegment<byte>(buf, 0, buf.Length);
                var task = this._ws.SendAsync(arSeg, WebSocketMessageType.Close, true, CancellationToken.None);
                task.Wait();
            }
            catch (Exception) { }
        }

        public void Close()
        {
            try
            {
                if (this._sock != null)
                {
                    this._sock.Close();
                }
                else
                {
                    _ = this._ws.CloseAsync(WebSocketCloseStatus.Empty, string.Empty, CancellationToken.None);
                }
            }
            catch (Exception) { }
        }

        public async Task Connect(string dstHost, int dstPort)
        {
            this._dstHost = dstHost;
            this._dstPort = dstPort;

            if (string.IsNullOrEmpty(this._wsEndPoint))
            {
                this._sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await this._sock.ConnectAsync(dstHost, dstPort);
                this._sock.NoDelay = true;
            }
            else
            {
                var uri = this._wsEndPoint;
                uri += dstHost.Replace('.', '/');
                uri += '/';
                uri += dstPort.ToString();

                this._ws = new ClientWebSocket();
                var uriObj = new Uri(uri);
                await this._ws.ConnectAsync(uriObj, CancellationToken.None);
            }
        }

        public async Task<bool> Send(byte[] buf, int offset, int size)
        {
            int nActualSize = 0;
            if (this._sock != null)
            {
                while (nActualSize < size)
                {
                    var arSeg = new ArraySegment<byte>(buf, offset + nActualSize, size - nActualSize);
                    var n = await this._sock.SendAsync(arSeg, SocketFlags.None);
                    if (n <= 0)
                    {
                        break;
                    }
                    nActualSize += n;
                }
            }
            else
            {
                var arSeg = new ArraySegment<byte>(buf, offset, size);
                await this._ws.SendAsync(arSeg, WebSocketMessageType.Binary, false, CancellationToken.None);
                nActualSize = size;
            }
            return (nActualSize == size);
        }

        public async Task<int> Recv(byte[] buf, int offset, int size)
        {
            int nActualSize = 0;
            if (this._sock != null)
            {
                var arSeg = new ArraySegment<byte>(buf, offset + nActualSize, size - nActualSize);
                var n = await this._sock.ReceiveAsync(arSeg, SocketFlags.None);
                if (n > 0)
                {
                    nActualSize += n;
                }
            }
            else
            {
                var arSeg = new ArraySegment<byte>(buf, offset, size);
                var r = await this._ws.ReceiveAsync(arSeg, CancellationToken.None);
                if (r != null)
                {
                    if (WebSocketMessageType.Binary == r.MessageType)
                    {
                        nActualSize = r.Count;
                    }
                }
            }
            return nActualSize;
        }

    }
}
