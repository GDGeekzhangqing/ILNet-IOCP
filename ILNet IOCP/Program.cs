using System;
using ILNet_IOCP.Core;


namespace ILNet_IOCP
{
    class Program
    {
        static void Main(string[] args)
        {
            IOCPServer IOCPServer = new IOCPServer(10, 1024);
            IOCPServer.Start(9900);
        }
    }
}
