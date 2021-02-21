using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ILNet_IOCP.Tools;

namespace ILNet_IOCP.Core
{
   public class EasyClient
    {
        public TcpClient _client;

        public int port;

        public IPAddress remote;

        public EasyClient(int port, IPAddress remote)
        {
            this.port = port;
            this.remote = remote;
        }

        public void connect()
        {
            this._client = new TcpClient();
            _client.Connect(remote, port);
            NetLogger.LogMsg("客户端尝试连接");
        }
        public void disconnect()
        {
            _client.Close();
            NetLogger.LogMsg("客户端断开连接");
        }
        public void send(string msg)
        {
            byte[] data = Encoding.Default.GetBytes(msg);
            _client.GetStream().Write(data, 0, data.Length);
            NetLogger.LogMsg("客户端发送数据");
        }
    }
}
