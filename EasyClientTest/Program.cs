using System;
using System.Configuration;
using System.Net;
using ILNet_IOCP.Core;

namespace EasyClientTest
{
    class Program
    {
        static void Main(string[] args)
        {
            EasyClient c = new EasyClient(SrvCfg.srvPort, IPAddress.Parse(SrvCfg.srvIP));

            c.connect();
            Console.WriteLine("服务器连接成功!");
            while (true)
            {
                Console.Write("send>");
                string msg = Console.ReadLine();
                if (msg == "exit")
                    break;
                c.send(msg);
            }
            c.disconnect();
            Console.ReadLine();
        }
    }
}
