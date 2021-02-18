using ILNet_IOCP.Tools;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ILNet_IOCP.Core
{

    // 实现套接字服务器的连接逻辑。
    // 在接受连接后，从客户端读取所有数据
    // 被发送回客户机。读取和回显到客户机模式
    // 一直持续到客户端断开连接。
    public abstract class IOCPServer<T> where T:NetMsg
    {
        #region 属性

        /// <summary>
        /// 样品的最大连接数设计为同时处理
        /// </summary>
        private int m_numConnections;

        /// <summary>
        /// 用于每个套接字I/O操作的缓冲区大小
        /// </summary>
        private int m_receiveBufferSize;

        /// <summary>
        /// 表示用于所有套接字操作的大型可重用缓冲区集
        /// </summary>
        private BufferManager m_bufferManager;

        /// <summary>
        /// 读，写(不要为接收对象分配缓冲区空间)
        /// </summary>
        private const int opsToPreAlloc = 2;

        /// <summary>
        /// 用于监听传入的连接请求的套接字      
        /// </summary>
        private Socket listenSocket;

        /// <summary>
        /// 用于写、读和接受套接字操作的可重用SocketAsyncEventArgs对象池
        /// </summary>
        private SocketAsyncEventArgsPool m_readWritePool;

        /// <summary>
        /// 服务器接收到的#字节总数的计数器
        /// </summary>
        private int m_totalBytesRead;

        /// <summary>
        /// 连接到服务器的客户端的总数
        /// </summary>
        private int m_numConnectedSockets;

        /// <summary>
        /// 最大客户端连接数
        /// </summary>
        private Semaphore m_maxNumberAcceptedClients;

        #endregion

        public int sessionID = 0;

        public Socket Client;

        public NetPkg pack = new NetPkg();
        /// <summary>
        /// 处理线程待处理的数据队列
        /// </summary>
        protected ConcurrentQueue<NetMsg> DataBeProcessed = new ConcurrentQueue<NetMsg>();

        #region 事件
        /// <summary>
        /// 首次开启连接的事件
        /// </summary>
        public event Action OnConnectEvent;

        /// <summary>
        /// 接收到数据时的委托事件
        /// </summary>
        public event Action<T> OnReciveMsgEvent;

        /// <summary>
        /// 关闭会话时的委托事件
        /// </summary>
        public event Action OnDisConnectEvent;

        #endregion

        #region 初始化

        /// <summary>
        /// 创建一个未初始化的服务器实例。
        /// 启动侦听连接请求的服务器
        /// 先调用Init方法，再调用Start方法
        /// </summary>
        /// <param name="numConnections">样品的最大连接数设计为同时处理</param>
        /// <param name="receiveBufferSize">用于每个套接字I</param>
        public IOCPServer(int numConnections, int receiveBufferSize)
        {
            m_totalBytesRead = 0;
            m_numConnectedSockets = 0;
            m_numConnections = numConnections;
            m_receiveBufferSize = receiveBufferSize;
            //分配缓冲区，使最大数量的套接字可以有一个未完成的读和
            //同时写入到套接字
            m_bufferManager = new BufferManager(receiveBufferSize * numConnections * opsToPreAlloc,
                receiveBufferSize);

            m_readWritePool = new SocketAsyncEventArgsPool(numConnections);
            m_maxNumberAcceptedClients = new Semaphore(numConnections, numConnections);
        }

        /// <summary>
        /// 通过预先分配可重用缓冲区来初始化服务器
        /// 上下文对象。 这些对象不需要预先分配
        /// 或重用，但这样做是为了说明API是如何做到的
        /// 易于用于创建可重用对象以提高服务器性能。
        /// </summary>
        public void Init()
        {
            // 分配一个大的字节缓冲区，所有的I/O操作都使用其中的一块。这gaurds
            // 对记忆的碎片
            m_bufferManager.InitBuffer();

            // 预分配SocketAsyncEventArgs对象池
            SocketAsyncEventArgs readWriteEventArg;

            for (int i = 0; i < m_numConnections; i++)
            {
                //预分配一组可重用的SocketAsyncEventArgs
                readWriteEventArg = new SocketAsyncEventArgs();
                readWriteEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(IOCP_Completed);
                readWriteEventArg.UserToken = new AsyncUserToken();

                // 从缓冲池中分配一个字节缓冲区给SocketAsyncEventArg对象
                m_bufferManager.SetBuffer(readWriteEventArg);

                // 将SocketAsyncEventArg添加到池中
                m_readWritePool.Push(readWriteEventArg);
            }
        }


        /// <summary>
        /// 此方法在套接字上完成接收或发送操作时调用
        /// </summary>
        /// <param name="sender">SocketAsyncEventArg与完成的接收操作相关联</param>
        /// <param name="e"></param>
        private void IOCP_Completed(object sender, SocketAsyncEventArgs e)
        {
            // 确定刚刚完成的操作类型，并调用关联的处理程序
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Accept:
                    OnConnected();
                    break;
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;
                case SocketAsyncOperation.Send:
                    ProcessSendCallBack(e);
                    break;
                case SocketAsyncOperation.Disconnect:
                    OnDisConnected();
                    break;
                default:
                    throw new ArgumentException("在套接字上完成的最后一个操作不是接收或发送");
            }
        }

        #endregion

        #region 连接服务器

        /// <summary>
        /// 启动正在侦听的服务器
        /// 传入的连接请求。
        /// </summary>
        /// <param name="localEndPoint">服务器将侦听的端点有关的连接请求</param>
        public void Start(Int32 port)
        {
            Init();

            //获得主机相关信息
            IPAddress[] addressList = Dns.GetHostEntry(Environment.MachineName).AddressList;
            IPEndPoint localEndPoint = new IPEndPoint(addressList[addressList.Length - 1], port);

            // 创建侦听传入连接的套接字
            listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            if (localEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // 配置监听socket为 dual-mode (IPv4 & IPv6) 
                // 27相当于下面的winsock代码片段中的IPV6_V6ONLY套接字选项，
                listenSocket.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)27, false);
                listenSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, localEndPoint.Port));
            }
            else
            {
                listenSocket.Bind(localEndPoint);
            }

            // 启动服务器，监听积压了100个连接
            listenSocket.Listen(m_numConnections);

            // post在监听套接字上接受
            StartAccept(null);

            //Console.WriteLine("{0} connected sockets with one outstanding receive posted to each....press any key", m_outstandingReadCount);
            Console.WriteLine("按任意键终止服务器进程....");
            Console.ReadKey();
        }

        /// <summary>
        /// 开始接受来自客户端的连接请求的操作
        /// </summary>
        /// <param name="acceptEventArg">发出时要使用的上下文对象,服务器侦听套接字上的accept操作</param>
        public void StartAccept(SocketAsyncEventArgs acceptEventArg)
        {
            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptEventArg_Completed);
            }
            else
            {
                // 套接字必须被清除，因为上下文对象正在被重用
                acceptEventArg.AcceptSocket = null;
            }

            // 阻塞当前线程以接收传入消息。
            m_maxNumberAcceptedClients.WaitOne();
            bool willRaiseEvent = listenSocket.AcceptAsync(acceptEventArg);
            if (!willRaiseEvent)
            {
                ProcessAccept(acceptEventArg);
            }
        }

        /// <summary>
        /// 这个方法是与Socket.AcceptAsync相关联的回调方法
        /// 操作，并在accept操作完成时调用
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        /// <summary>
        /// 监听Socket接收处理
        /// </summary>
        /// <param name="e"></param>
        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            Interlocked.Increment(ref m_numConnectedSockets);
            Console.WriteLine("接受客户端连接! 这里有 {0} 个 客户端 连接到了服务器",
                m_numConnectedSockets);

            //获取所接受的客户端连接的套接字，并将其放入
            //ReadEventArg对象用户令牌
            SocketAsyncEventArgs readEventArgs = m_readWritePool.Pop();
            ((AsyncUserToken)readEventArgs.UserToken).Socket = e.AcceptSocket;

            // 一旦连接到客户机，就向连接发送一个receive
            bool willRaiseEvent = e.AcceptSocket.ReceiveAsync(readEventArgs);
            if (!willRaiseEvent)
            {
                ProcessReceive(readEventArgs);
            }

            // 接受下一个连接请求
            StartAccept(e);
        }
        #endregion

        #region 发送消息

        public void SendMsg(T msg)
        {
            byte[] data = AnalysisMsg.PackLenInfo(AnalysisMsg.Serialize<T>(msg));
            SendMsg(data);
        }

        public void SendMsg(byte[] data)
        {
            if (data.Length < 65007)
                listenSocket.SendTo(data,Client.LocalEndPoint);
            else
                Console.WriteLine("数据太大");
        }

        /// <summary>
        /// 此方法在异步接收操作完成时调用。
        ///  如果远程主机关闭了连接，则套接字将被关闭。
        ///  如果接收到数据，则将数据回显到客户端。
        /// </summary>
        /// <param name="e"></param>
        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            // 检查远程主机是否关闭了连接
            AsyncUserToken token = (AsyncUserToken)e.UserToken;
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                Socket s = (Socket)e.UserToken;
                if (s.Available == 0)
                {
                    //增加服务器接收到的总字节数
                    Interlocked.Add(ref m_totalBytesRead, e.BytesTransferred);
                    Console.WriteLine("服务器总共读取了{0}字节", m_totalBytesRead);

                    //处理数据
                    DealWithData(e,s);
                   
                    //将接收到的数据回传给客户端
                    //e.SetBuffer(e.Offset, e.BytesTransferred);
                    //投递发送请求，这个函数有可能同步发送出去，这时返回false，并且不会引发SocketAsyncEventArgs.Completed事件
                    //bool willRaiseEvent = token.Socket.SendAsync(e);
                    //if (!willRaiseEvent)
                    //{
                    //    // 同步发送时处理发送完成事件
                    //    ProcessSendCallBack(e);
                    //}
                }
                else if (!s.ReceiveAsync(e))    //为接收下一段数据，投递接收请求，这个函数有可能同步完成，这时返回false，并且不会引发SocketAsyncEventArgs.Completed事件
                {
                    //同步接收时处理接收完成事件
                    ProcessReceive(e);
                }
            }
            else
            {
                CloseClientSocket(e);
            }
        }

        /// <summary>
        /// 此方法在异步发送操作完成时调用。
        /// 该方法在套接字上发出另一个receive以读取任何额外的数据
        /// 从客户机发送的数据
        /// </summary>
        /// <param name="e"></param>
        private void ProcessSendCallBack(SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                // 完成回显数据到客户端
                AsyncUserToken token = (AsyncUserToken)e.UserToken;
                // 读取从客户端发送的下一个数据块
                bool willRaiseEvent = token.Socket.ReceiveAsync(e);
                if (!willRaiseEvent)
                {
                    ProcessReceive(e);
                }
            }
            else
            {
                ProcessError(e);
            }
        }

        /// <summary>
        /// 处理socket错误
        /// </summary>
        /// <param name="e"></param>
        private void ProcessError(SocketAsyncEventArgs e)
        {
            Socket s = e.UserToken as Socket;
            IPEndPoint ip = s.LocalEndPoint as IPEndPoint;

            this.CloseClientSocket(e);
            Console.WriteLine(String.Format("套接字错误 {0}, IP {1}, 操作 {2}。", (Int32)e.SocketError, ip, e.LastOperation));
        }

        #endregion

        #region 关闭连接

        /// <summary>
        /// 关闭与客户端的连接
        /// </summary>
        /// <param name="e"></param>
        private void CloseClientSocket(SocketAsyncEventArgs e)
        {
            AsyncUserToken token = e.UserToken as AsyncUserToken;

            // 关闭与客户端关联的套接字
            try
            {
                token.Socket.Shutdown(SocketShutdown.Send);
            }
            catch (Exception)
            {
                //如果客户端已经关闭，则抛出，因此不需要捕获。
            }
            token.Socket.Close();
            // 减少跟踪连接到服务器的客户机总数的计数器
            Interlocked.Decrement(ref m_numConnectedSockets);

            // 释放SocketAsyncEventArg，以便它们可以被另一个客户机重用
            m_readWritePool.Push(e);

            m_maxNumberAcceptedClients.Release();
            Console.WriteLine("客户端已与服务器断开连接。有{0}客户端连接到服务器", m_numConnectedSockets);
        }

        /// <summary>
        /// 停止服务
        /// </summary>
        public void Stop()
        {
            this.listenSocket.Close();
            m_maxNumberAcceptedClients.Release();
        }
        #endregion

        /// <summary>
        /// 处理数据
        /// </summary>
        /// <param name="e"></param>
        public void DealWithData(SocketAsyncEventArgs e,Socket s)
        {
            byte[] data = new byte[e.BytesTransferred];
            T msg = AnalysisMsg.DeSerialize<T>(data);
            Array.Copy(e.Buffer, e.Offset, data, 0, data.Length);//从e.Buffer块中复制数据出来，保证它可重用
            OnReciveMsgEvent?.Invoke(msg);
        }

        #region 事件回调

        /// <summary>
        /// 连接网络
        /// </summary>
        protected virtual void OnConnected()
        {
            NetLogger.LogMsg($"客户端{sessionID}上线，上线时间：", LogLevel.Info);
        }

        /// <summary>
        /// 接收网络消息
        /// </summary>
        /// <param name="msg"></param>
        protected virtual void OnReciveMsg(T msg)
        {
            //更新心跳包    
            NetLogger.LogMsg($"接收网络消息：{msg}", LogLevel.Info);
        }

        /// <summary>
        /// 断开网络连接
        /// </summary>
        protected virtual void OnDisConnected()
        {
            NetLogger.LogMsg($"客户端{sessionID}离线，离线时间：{DateTime.UtcNow}\t", LogLevel.Info);
        }

        #endregion
    }
}
