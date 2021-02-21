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

    /// <summary>
    /// 实现套接字服务器的连接逻辑。
    /// 在接受连接后，从客户端读取所有数据
    /// 被发送回客户机。读取和回显到客户机模式
    /// 一直持续到客户端断开连接。
    /// </summary>
    public class IOCPServer : IDisposable
    {
        #region Fields

        /// <summary>
        ///设计为同时处理客户端的最大连接数
        /// </summary>
        private int m_numConnections = 100;

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

        public int sessionID = 0;

        public NetPkg pack = new NetPkg();

        /// <summary>
        /// 已经连接的对象池
        /// </summary>
        internal ConcurrentDictionary<int, AsyncUserToken> connectClient;

        /// <summary>
        /// 处理线程待处理的数据队列
        /// </summary>
        protected ConcurrentQueue<NetMsg> DataBeProcessed;

        private bool disposed = false;

        #endregion

        #region Event
        /// <summary>
        /// 首次开启连接的事件
        /// </summary>
        public event Action OnConnectEvent;

        /// <summary>
        /// 接收到数据时的委托事件
        /// </summary>
        public event Action OnReciveMsgEvent;

        /// <summary>
        /// 关闭会话时的委托事件
        /// </summary>
        public event Action<int> OnDisConnectEvent;

        #endregion

        #region Properties

        /// <summary>
        /// 服务器是否正在运行
        /// </summary>
        public bool IsRunning { get; private set; }
        /// <summary>
        /// 监听的IP地址
        /// </summary>
        public IPAddress Address { get; private set; }
        /// <summary>
        /// 监听的端口
        /// </summary>
        public int Port { get; private set; }
        /// <summary>
        /// 通信使用的编码
        /// </summary>
        public Encoding Encoding { get; set; }

        #endregion

        #region Ctors
        /// <summary>
        /// 异步IOCP SOCKET 服务器
        /// </summary>
        /// <param name="listenPort"></param>
        /// <param name="numConnections"></param>
        public IOCPServer(int listenPort, int numConnections, int receiveBufferSize) :
            this(IPAddress.Any, listenPort, numConnections, receiveBufferSize)
        { }

        public IOCPServer(IPEndPoint localEp, int numConnections, int receiveBufferSize) :
            this(localEp.Address, localEp.Port, numConnections, receiveBufferSize)
        { }

        /// <summary>
        /// 创建一个未初始化的服务器实例。
        /// 启动侦听连接请求的服务器
        /// 先调用Init方法，再调用Start方法
        /// </summary>
        /// <param name="numConnections">样品的最大连接数设计为同时处理</param>
        /// <param name="receiveBufferSize">用于每个套接字I</param>
        public IOCPServer(IPAddress localIPAddress, int listenPort, int numConnections, int receiveBufferSize)
        {
            this.Address = localIPAddress;
            this.Port = listenPort;
            this.Encoding = Encoding.Default;

            this.m_totalBytesRead = 0;
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
            DataBeProcessed = new ConcurrentQueue<NetMsg>();

            connectClient = new ConcurrentDictionary<int, AsyncUserToken>();

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

        #endregion

        #region Start

        /// <summary>
        /// 启动正在侦听的服务器
        /// 传入的连接请求。
        /// </summary>
        /// <param name="localEndPoint">服务器将侦听的端点有关的连接请求</param>
        public void Start()
        {
            if (!IsRunning)
            {
                Init();
                IsRunning = true;

                //获得主机相关信息
                IPEndPoint localEndPoint = new IPEndPoint(Address, Port);

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

                // 启动服务器，监听积压了m_numConnections个连接
                listenSocket.Listen(m_numConnections);
                // post在监听套接字上接受
                StartAccept(null);

                //心跳检测
                //TODO

                //Console.WriteLine("{0} connected sockets with one outstanding receive posted to each....press any key", m_outstandingReadCount);
                NetLogger.LogMsg("按任意键终止服务器进程....");
                Console.ReadKey();
            }
        }

        #endregion

        #region Stop
        /// <summary>
        /// 停止服务
        /// </summary>
        public void Stop()
        {
            if (IsRunning)
            {
                IsRunning = false;
                this.listenSocket.Close();
                //关闭对所有客户端的连接
                m_maxNumberAcceptedClients.Release();
            }
        }

        #endregion

        #region Accept

        /// <summary>
        /// 开始接受来自客户端的连接请求的操作
        /// </summary>
        /// <param name="acceptEventArg">发出时要使用的上下文对象,服务器侦听套接字上的accept操作</param>
        public void StartAccept(SocketAsyncEventArgs acceptEventArg)
        {
            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(OnAcceptCompleted);
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
                //投递请求
                //如果I/O挂起等待异步则触发AcceptAsyn_Asyn_Completed事件
                //此时I/O操作同步完成，不会触发Asyn_Completed事件，所以指定BeginAccept()方法
                ProcessAccept(acceptEventArg);
            }
        }

        /// <summary>
        /// 这个方法是与Socket.AcceptAsync相关联的回调方法
        /// 操作，并在accept操作完成时调用
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnAcceptCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        /// <summary>
        /// 监听Socket接收处理回调
        /// </summary>
        /// <param name="e"></param>
        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                //和客户端关联的socket
                Socket s = e.AcceptSocket;
                if (s.Connected)
                {
                    try
                    {
                        Interlocked.Increment(ref m_numConnectedSockets);

                        //获取所接受的客户端连接的套接字，并将其放入
                        //ReadEventArg对象用户令牌
                        SocketAsyncEventArgs readEventArgs = m_readWritePool.Pop();
                        ((AsyncUserToken)readEventArgs.UserToken).Socket = e.AcceptSocket;
                        readEventArgs.UserToken = s;
                        //连接成功的回调
                        OnConnected();
                        OnConnectEvent?.Invoke();
                        NetLogger.LogMsg($"接受客户端连接! 这里有 { m_numConnectedSockets} 个 客户端 连接到了服务器", LogLevel.Info);
                        //一旦连接到客户机，就向连接发送一个receive
                        bool willRaiseEvent = e.AcceptSocket.ReceiveAsync(readEventArgs);
                        if (!willRaiseEvent)
                        { 
                            ProcessReceive(readEventArgs);
                        }
                        else
                        {
                            sessionID++;
                            AsyncUserToken asyncUser = new AsyncUserToken();
                            //添加连接的客户端到集合中
                            asyncUser.Socket = readEventArgs.AcceptSocket;   
                            connectClient.TryAdd(sessionID, asyncUser);
                        }
                    }
                    catch (SocketException ex)
                    {
                        NetLogger.LogMsg($"接收客户端{s.RemoteEndPoint}数据出错，异常信息：{ex.ToString()}.", LogLevel.Error);
                        //异常处理
                        //TODO
                    }

                    // 接受下一个连接请求
                    StartAccept(e);
                }
            }
        }
        #endregion

        #region Send

        /// <summary>
        /// 异步的发送数据
        /// </summary>
        /// <param name="e"></param>
        /// <param name="data"></param>
        public void Send(SocketAsyncEventArgs e, byte[] data)
        {
            if (e.SocketError == SocketError.Success)
            {
                Socket s = e.AcceptSocket;//和客户端关联的socket
                if (s.Connected)
                {
                    Array.Copy(data, 0, e.Buffer, 0, data.Length);//设置发送数据

                    //e.SetBuffer(data, 0, data.Length); //设置发送数据
                    if (!s.SendAsync(e))//投递发送请求，这个函数有可能同步发送出去，这时返回false，并且不会引发SocketAsyncEventArgs.Completed事件
                    {
                        // 同步发送时处理发送完成事件
                        ProcessSend(e);
                    }
                    else
                    {
                        CloseClientSocket(e);
                    }
                }
            }
        }

        /// <summary>
        /// 同步的使用socket发送数据
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="size"></param>
        /// <param name="timeout"></param>
        public void Send(Socket socket, byte[] buffer, int offset, int size, int timeout)
        {
            socket.SendTimeout = 0;
            int startTickCount = Environment.TickCount;
            int sent = 0; // 已经发送了多少字节
            do
            {
                if (Environment.TickCount > startTickCount + timeout)
                {
                    throw new Exception("Timeout...");
                }
                try
                {
                    sent += socket.Send(buffer, offset + sent, size - sent, SocketFlags.None);
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.WouldBlock ||
                    ex.SocketErrorCode == SocketError.IOPending ||
                    ex.SocketErrorCode == SocketError.NoBufferSpaceAvailable)
                    {
                        // 套接字缓冲区可能已满，请等待并重试
                        Thread.Sleep(30);
                    }
                    else
                    {
                        throw ex; // 任何严重的错误发生
                    }
                }
            } while (sent < size);
        }

        /// <summary>
        /// 此方法在异步发送操作完成时调用。
        /// 该方法在套接字上发出另一个receive以读取任何额外的数据
        /// 从客户机发送的数据
        /// </summary>
        /// <param name="e"></param>
        private void ProcessSend(SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                Socket s = (Socket)e.UserToken;

                //// 完成回显数据到客户端
                //AsyncUserToken token = (AsyncUserToken)e.UserToken;
                //// 读取从客户端发送的下一个数据块
                //bool willRaiseEvent = token.Socket.ReceiveAsync(e);
                //if (!willRaiseEvent)
                //{
                //    ProcessReceive(e);
                //}
            }
            else
            {
                CloseClientSocket(e);
            }
        }

        #endregion

        #region Receive

        /// <summary>
        /// 此方法在异步接收操作完成时调用。
        /// 如果远程主机关闭了连接，则套接字将被关闭。
        /// 如果接收到数据，则将数据回显到客户端。
        /// </summary>
        /// <param name="e"></param>
        private void ProcessReceive(SocketAsyncEventArgs e, Action<int> closeCB = null)
        {
            // 检查远程主机是否关闭了连接
            //AsyncUserToken token = (AsyncUserToken)e.UserToken;
            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                //添加监听
                this.OnDisConnectEvent += closeCB;

                Socket s = (Socket)e.UserToken;
                //判断所有需要接收的数据是否已经完成
                if (s.Available == 0)
                {
                    //从侦听者获取接收到的消息。 
                    //String received = Encoding.ASCII.GetString(e.Buffer, e.Offset, e.BytesTransferred);
                    //echo the data received back to the client
                    //e.SetBuffer(e.Offset, e.BytesTransferred);

                    byte[] data = new byte[e.BytesTransferred];
                    Array.Copy(e.Buffer, e.Offset, data, 0, data.Length);//从e.Buffer块中复制数据出来，保证它可重用

                    string info = Encoding.Default.GetString(data);
                    NetLogger.LogMsg($"收到{s.RemoteEndPoint.ToString()}的数据：{info}");

                    //将接收到的数据回传给客户端
                    //e.SetBuffer(e.Offset, e.BytesTransferred);
                    //投递发送请求，这个函数有可能同步发送出去，这时返回false，并且不会引发SocketAsyncEventArgs.Completed事件
                    //bool willRaiseEvent = token.Socket.SendAsync(e);
                    //if (!willRaiseEvent)
                    //{
                    //    // 同步发送时处理发送完成事件
                    //    ProcessSendCallBack(e);
                    //}

                    // 处理数据  
                    //DealWithData(e, s);

                    //增加服务器接收到的总字节数
                    Interlocked.Add(ref m_totalBytesRead, e.BytesTransferred);
                    NetLogger.LogMsg($"服务器总共读取了{m_totalBytesRead}字节", LogLevel.Info);
                }
                if (!s.ReceiveAsync(e))    //为接收下一段数据，投递接收请求，这个函数有可能同步完成，这时返回false，并且不会引发SocketAsyncEventArgs.Completed事件
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
        /// 处理socket错误
        /// </summary>
        /// <param name="e"></param>
        private void ProcessError(SocketAsyncEventArgs e)
        {
            Socket s = e.UserToken as Socket;
            IPEndPoint ip = s.LocalEndPoint as IPEndPoint;

            this.CloseClientSocket(e);
            NetLogger.LogMsg(String.Format($"套接字错误 {(Int32)e.SocketError}, IP {ip}, 操作 { e.LastOperation}。", LogLevel.Error));
        }

        #endregion

        #region EventCallBack

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
                    ProcessAccept(e);
                    break;
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;
                default:
                    throw new ArgumentException("在套接字上完成的最后一个操作不是接收或发送");
            }
        }

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
        protected virtual void OnReciveMsg(NetMsg msg)
        {
            //更新心跳包    
            NetLogger.LogMsg($"接收网络消息：{msg}", LogLevel.Info);
        }

        /// <summary>
        /// 断开网络连接
        /// </summary>
        protected virtual void OnDisConnected(int sessionId)
        {
            NetLogger.LogMsg($"客户端{sessionID}离线，离线时间：{DateTime.UtcNow}\t", LogLevel.Info);
        }
        #endregion

        #region Close

        /// <summary>
        /// 客户端断开一个连接
        /// </summary>
        /// <param name="sessionId"></param>
        public void Close(int sessionId)
        {
            AsyncUserToken asyncUser;
            if (!connectClient.TryGetValue(sessionId, out asyncUser))
            {
                return;
            }
            CloseClientSocket(asyncUser.AsyncEventArgs);
        }

        /// <summary>
        /// 关闭与客户端的连接
        /// </summary>
        /// <param name="e"></param>
        private void CloseClientSocket(SocketAsyncEventArgs e)
        {
            //AsyncUserToken token = e.UserToken as AsyncUserToken;
            Socket token = e.UserToken as Socket;
            // 关闭与客户端关联的套接字
            try
            {
                token.Shutdown(SocketShutdown.Send);
            }
            catch (Exception)
            {
                //如果客户端已经关闭，则抛出，因此不需要捕获。
            }
            finally
            {
                token.Close();
            }


            // 减少跟踪连接到服务器的客户机总数的计数器
            Interlocked.Decrement(ref m_numConnectedSockets);
            // 释放SocketAsyncEventArg，以便它们可以被另一个客户机重用
            m_readWritePool.Push(e);
            m_maxNumberAcceptedClients.Release();
            NetLogger.LogMsg($"客户端已与服务器断开连接。有{ m_numConnectedSockets}客户端连接到服务器", LogLevel.Error);
        }

        #endregion

        #region Dispose
        /// <summary>
        ///执行与释放相关的应用程序定义的任务，
        ///释放或重置非托管资源。
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放非托管和(可选)托管资源
        /// </summary>
        /// <param name="disposing">true 释放托管和非托管资源; false 仅释放非托管资源</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    try
                    {
                        Stop();
                        if (listenSocket != null)
                        {
                            listenSocket = null;
                        }
                    }
                    catch (SocketException ex)
                    {
                        //TODO 事件
                    }
                }
                disposed = true;
            }
        }

        #endregion

    }

}







