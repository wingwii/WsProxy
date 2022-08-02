using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace server
{
    class WsProxy
    {
        private HttpContext _ctx = null;
        private WebSocket _ws = null;
        private Socket _sock = null;
        private int _dstPort = 0;
        private string _dstHost = string.Empty;

        public WsProxy(HttpContext ctx, WebSocket ws)
        {
            this._ctx = ctx;
            this._ws = ws;
        }

        public static int ProxyBufferSize { get; set; } = 16384;

        public override string ToString()
        {
            return (this._dstHost + ":" + this._dstPort.ToString());
        }

        public async Task Run()
        {
            var uri = this._ctx.Request.Path.Value;
            if (!uri.StartsWith("/ws/", StringComparison.Ordinal))
            {
                return;
            }

            uri = uri.Substring(4);
            var parts = uri.Split('/');
            var n = parts.Length;
            if (n < 2)
            {
                return;
            }

            --n;
            this._dstPort = int.Parse(parts[n]);
            this._dstHost = string.Empty;
            for (int i = 0; i < n; ++i)
            {
                if (i > 0)
                {
                    this._dstHost += '.';
                }
                this._dstHost += parts[i];
            }

            this._sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await this.RunProxy();
            }
            catch (Exception) { }

            try { this._sock.Close(); }
            catch (Exception) { }
            try { this._sock.Dispose(); }
            catch (Exception) { }
        }

        private async Task RunProxy()
        {
            await this._sock.ConnectAsync(this._dstHost, this._dstPort);
            this._sock.NoDelay = true;

            _ = this.TransferRemoteSock();
            await this.TransferLocalWS();
        }

        private void SockShutdown()
        {
            try { this._sock.Shutdown(SocketShutdown.Both); }
            catch (Exception) { }
        }

        private async Task WsShutdown()
        {
            try
            {
                var buf = new byte[4];
                var arSeg = new ArraySegment<byte>(buf, 0, buf.Length);
                await this._ws.SendAsync(arSeg, WebSocketMessageType.Close, true, CancellationToken.None);
            }
            catch (Exception) { }
        }

        private async Task<bool> SockSend(byte[] buf, int offset, int size)
        {
            int nActualSize = 0;
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
            return (nActualSize == size);
        }

        private async Task TransferRemoteSock()
        {
            var bufSize = ProxyBufferSize;
            var buf = new byte[bufSize];
            while (true)
            {
                var arSeg = new ArraySegment<byte>(buf, 0, bufSize);
                var n = await this._sock.ReceiveAsync(arSeg, SocketFlags.None);
                if (n <= 0)
                {
                    break;
                }

                arSeg = new ArraySegment<byte>(buf, 0, n);
                await this._ws.SendAsync(arSeg, WebSocketMessageType.Binary, false, CancellationToken.None);
            }
            await this.WsShutdown();
            this.SockShutdown();
        }

        private async Task TransferLocalWS()
        {
            var bufSize = ProxyBufferSize;
            var buf = new byte[bufSize];
            while (true)
            {
                var arSeg = new ArraySegment<byte>(buf, 0, bufSize);
                var r = await this._ws.ReceiveAsync(arSeg, CancellationToken.None);
                if (null == r)
                {
                    break;
                }
                if (r.MessageType != WebSocketMessageType.Binary)
                {
                    break;
                }

                var n = r.Count;
                if (n <= 0)
                {
                    break;
                }

                var sendOK = await this.SockSend(buf, 0, n);
                if (!sendOK)
                {
                    break;
                }
            }
            await this.WsShutdown();
            this.SockShutdown();
        }

    }
}
