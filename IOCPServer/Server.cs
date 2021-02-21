using System;
using System.Net;
using ILNet_IOCP.Core;

namespace IOCPServerTest
{
    class Server
    {
        static void Main(string[] args)
        {
            IOCPServer server = new IOCPServer(IPAddress.Parse(SrvCfg.srvIP),SrvCfg.srvPort,100, 1024);
            server.Start();
            Console.WriteLine("服务器已启动....");
            System.Console.ReadLine();
        }
    }
}
