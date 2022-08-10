using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;

namespace client
{
    class ServerHandler
    {
        private Socket _sock = null;
        private string _wsEndPoint = string.Empty;
        private string _fwdEndPoint = null;
        private byte[] _tmpBuf = null;
        private byte[] _firstSendBuf = null;
        private BufferReader _bufRdr = new BufferReader();
        private string _rawHttpRqHdr = string.Empty;
        private string _httpRqMethod = string.Empty;
        private string _httpRqURI = string.Empty;
        private string _dstHost = string.Empty;
        private int _dstPort = 0;
        private ProxyConnector _dstConn = null;

        public ServerHandler(Socket sock, string wsEndPoint, string fwdEndPoint)
        {
            this._sock = sock;
            this._wsEndPoint = wsEndPoint;
            this._fwdEndPoint = fwdEndPoint;
        }

        public enum ProxyModeEnum
        {
            None = 0,
            PortForward,
            Http,
            Https,
            Socks5,
        }

        public ProxyModeEnum ProxyMode { get; private set; } = ProxyModeEnum.None;
        public static int HttpRequestHeaderMaxLength { get; set; } = 2048;
        public static int ProxyBufferSize { get; set; } = 16384;

        public async Task Run()
        {
            this._sock.NoDelay = true;

            this._tmpBuf = new byte[512];
            if (string.IsNullOrEmpty(this._fwdEndPoint))
            {
                var handshakingOK = await this.ProxyServerHandshake();
                if (!handshakingOK)
                {
                    return;
                }
            }
            else
            {
                var dstInfo = this._fwdEndPoint.Split(':');
                if (dstInfo.Length != 2)
                {
                    return;
                }

                this._dstHost = dstInfo[0];
                this._dstPort = int.Parse(dstInfo[1]);
                this.ProxyMode = ProxyModeEnum.PortForward;
            }

            var wsEndPoint = this._wsEndPoint;
            this._dstConn = new ProxyConnector(wsEndPoint);
            try
            {
                await this.RunProxy();
            }
            catch (Exception err)
            {
                err = null;
            }
            try { this._dstConn.Close(); }
            catch (Exception) { }
        }

        private async Task<bool> ProxyServerHandshake()
        {
            var buf = this._tmpBuf;
            var n = await this.SockRecv(buf, 0, 2, true);
            if (n != 2)
            {
                return false;
            }

            var result = false;
            var firstByte = (byte)buf[0];
            if (5 == firstByte)
            {
                result = await this.Socks5Handshake();
            }
            else if (firstByte >= 65 && firstByte <= 122)
            {
                result = await this.HttpHandshake();
            }

            return result;
        }

        private async Task RunProxy()
        {
            await this._dstConn.Connect(this._dstHost, this._dstPort);

            var buf = this._firstSendBuf;
            if (buf != null)
            {
                await this._dstConn.Send(buf, 0, buf.Length);
            }

            buf = this._bufRdr.PickRemainData();
            if (buf != null)
            {
                await this._dstConn.Send(buf, 0, buf.Length);
            }

            var bufSize = ProxyBufferSize;
            _ = this.ThreadProxy2(bufSize);
            await this.ThreadProxy1(bufSize);
        }

        private async Task ThreadProxy1(int bufSize)
        {
            var buf = new byte[bufSize];
            while (true)
            {
                var n = await this.SockRecv(buf, 0, bufSize, false);
                if (n <= 0)
                {
                    break;
                }

                var sendOK = await this._dstConn.Send(buf, 0, n);
                if (!sendOK)
                {
                    break;
                }
            }
            this.SockShutdown();
            this._dstConn.Shutdown();
        }

        private async Task ThreadProxy2(int bufSize)
        {
            var buf = new byte[bufSize];
            while (true)
            {
                var n = await this._dstConn.Recv(buf, 0, bufSize);
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
            this.SockShutdown();
            this._dstConn.Shutdown();
        }

        private void SockShutdown()
        {
            try { this._sock.Shutdown(SocketShutdown.Both); }
            catch (Exception) { }
        }

        private async Task<int> SockRecv(byte[] buf, int offset, int size, bool waitAll)
        {
            int nActualSize = 0;
            while (nActualSize < size)
            {
                var arSeg = new ArraySegment<byte>(buf, offset + nActualSize, size - nActualSize);
                var n = await this._sock.ReceiveAsync(arSeg, SocketFlags.None);
                if (n <= 0)
                {
                    break;
                }
                nActualSize += n;
                if (!waitAll)
                {
                    break;
                }
            }
            return nActualSize;
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

        private async Task<bool> Socks5Handshake()
        {
            var buf = this._tmpBuf;
            var version = (uint)buf[0];
            var methodCount = (int)buf[1];
            var n = await this.SockRecv(buf, 0, methodCount, true);
            if (n != methodCount)
            {
                return false;
            }

            var methodAccepted = false;
            for (int i = 0; i < methodCount; ++i)
            {
                if (0 == buf[i])
                {
                    methodAccepted = true;
                }
            }
            if (!methodAccepted)
            {
                return false;
            }

            buf[0] = (byte)version;
            buf[1] = 0;
            var sendOK = await this.SockSend(buf, 0, 2);
            if (!sendOK)
            {
                return false;
            }

            n = await this.SockRecv(buf, 0, 5, true);
            if (n != 5)
            {
                return false;
            }

            var cmd = (int)buf[1];
            if (cmd != 1)
            {
                return false;
            }

            var nextAddrLen = (int)0;
            var addrType = (int)buf[3];
            if (1 == addrType)
            {
                nextAddrLen = 3;
            }
            else if (3 == addrType)
            {
                nextAddrLen = (int)buf[4];
            }
            else if (4 == addrType)
            {
                nextAddrLen = 15;
            }

            if (nextAddrLen <= 0)
            {
                return false;
            }

            nextAddrLen += 2;
            n = await this.SockRecv(buf, 5, nextAddrLen, true);
            if (n != nextAddrLen)
            {
                return false;
            }

            this._dstPort = (int)buf[nextAddrLen + 3];
            this._dstPort *= 256;
            this._dstPort += (int)buf[nextAddrLen + 4];

            this._dstHost = string.Empty;
            if (1 == addrType)
            {
                var ipAddrBytes = new byte[4];
                Array.Copy(buf, 4, ipAddrBytes, 0, ipAddrBytes.Length);
                var ipv4 = new IPAddress(ipAddrBytes);
                this._dstHost = ipv4.ToString();
            }
            else if (3 == addrType)
            {
                this._dstHost = Encoding.ASCII.GetString(buf, 5, nextAddrLen - 2);
            }
            else if (4 == addrType)
            {
                var ipAddrBytes = new byte[16];
                Array.Copy(buf, 4, ipAddrBytes, 0, ipAddrBytes.Length);
                var ipv4 = new IPAddress(ipAddrBytes);
                this._dstHost = ipv4.ToString();
            }

            buf[0] = (byte)version;
            buf[1] = 0;
            buf[2] = 0;
            buf[3] = 1;
            sendOK = await this.SockSend(buf, 0, 10);
            if (!sendOK)
            {
                return false;
            }

            this.ProxyMode = ProxyModeEnum.Socks5;
            return true;
        }

        private async Task<bool> PrepareBufRdr()
        {
            if (this._bufRdr.Available <= 0)
            {
                var buf = this._tmpBuf;
                var n = await this.SockRecv(buf, 0, buf.Length, false);
                if (n <= 0)
                {
                    return false;
                }
                this._bufRdr.Feed(buf, 0, n);
            }
            return true;
        }

        private async Task<string> RecvUntilWhiteSpace(int maxLen)
        {
            var result = new StringBuilder();
            while (true)
            {
                var ok = await this.PrepareBufRdr();
                if (!ok)
                {
                    return null;
                }

                var b = this._bufRdr.ReadByte();
                if (32 == b)
                {
                    break;
                }
                if (result.Length >= maxLen)
                {
                    return null;
                }

                result.Append((char)b);
            }
            return result.ToString();
        }

        private async Task<bool> RecvRawHttpRqHdr()
        {
            var sb = new StringBuilder();
            sb.Append(this._httpRqMethod + " " + this._dstHost + ":" + this._dstPort.ToString() + " ");

            int hdrEndLen = 0;
            var maxLen = HttpRequestHeaderMaxLength;
            var buf = this._tmpBuf;
            while (true)
            {
                var ok = await this.PrepareBufRdr();
                if (!ok)
                {
                    return false;
                }

                var c = (char)this._bufRdr.ReadByte();
                sb.Append(c);
                if ('\r' == c || '\n' == c)
                {
                    if ('\r' == c)
                    {
                        if (0 == hdrEndLen || 2 == hdrEndLen)
                        {
                            ++hdrEndLen;
                        }
                        else
                        {
                            hdrEndLen = 0;
                        }
                    }
                    else if ('\n' == c)
                    {
                        if (1 == hdrEndLen)
                        {
                            ++hdrEndLen;
                        }
                        else if (3 == hdrEndLen)
                        {
                            break;
                        }
                        else
                        {
                            hdrEndLen = 0;
                        }
                    }
                }

                if (sb.Length > maxLen)
                {
                    return false;
                }
            }
            this._rawHttpRqHdr = sb.ToString();
            return true;
        }

        private async Task<bool> HttpHandshake()
        {
            var buf = this._tmpBuf;
            this._httpRqMethod = string.Empty + (char)buf[0] + (char)buf[1];
            this._httpRqURI = string.Empty;
            this._rawHttpRqHdr = string.Empty;
            this._firstSendBuf = null;

            var ok = false;
            var nextPart = await this.RecvUntilWhiteSpace(32);
            if (null == nextPart)
            {
                return false;
            }
            this._httpRqMethod = (this._httpRqMethod + nextPart).ToUpper();

            nextPart = await this.RecvUntilWhiteSpace(HttpRequestHeaderMaxLength);
            if (null == nextPart)
            {
                return false;
            }
            this._httpRqURI = nextPart;
            this._dstHost = this._httpRqURI;

            int pos = 0;
            if (this._httpRqMethod.Equals("CONNECT", StringComparison.Ordinal))
            {
                this._httpRqURI = string.Empty;
                this.ProxyMode = ProxyModeEnum.Https;
            }
            else
            {
                this._dstPort = 80;
                pos = this._dstHost.IndexOf("://", StringComparison.Ordinal);
                if (pos >= 0)
                {
                    var protocol = this._dstHost.Substring(0, pos).ToLower();
                    this._dstHost = this._dstHost.Substring(pos + 3);
                    if (protocol.Equals("https", StringComparison.Ordinal))
                    {
                        this._dstPort = 443;
                    }
                }

                pos = this._dstHost.IndexOf('/');
                if (pos < 0)
                {
                    this._httpRqURI = "/";
                }
                else
                {
                    this._httpRqURI = this._dstHost.Substring(pos);
                    this._dstHost = this._dstHost.Substring(0, pos);
                }

                var firstSendStr = this._httpRqMethod + " " + this._httpRqURI + " ";
                this._firstSendBuf = Encoding.ASCII.GetBytes(firstSendStr);

                this.ProxyMode = ProxyModeEnum.Http;
            }

            pos = this._dstHost.IndexOf(':');
            if (pos >= 0)
            {
                var portStr = this._dstHost.Substring(pos + 1);
                this._dstHost = this._dstHost.Substring(0, pos);
                this._dstPort = int.Parse(portStr);
            }

            if (ProxyModeEnum.Https == this.ProxyMode)
            {
                ok = await this.RecvRawHttpRqHdr();

                var respBuf = Encoding.ASCII.GetBytes("HTTP/1.0 200 OK\r\n\r\n");
                await SockSend(respBuf, 0, respBuf.Length);

                this._bufRdr.Clear();
            }

            return true;
        }

    }
}
