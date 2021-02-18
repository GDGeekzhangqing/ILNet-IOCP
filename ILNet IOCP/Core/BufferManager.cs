using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace ILNet_IOCP.Core
{
    // 这个类创建一个可以被分割的大缓冲区
    // 并赋值给SocketAsyncEventArgs对象，以便与每个对象一起使用
    // 套接字I / O操作。
    // 这使得缓冲区易于重用和防范
    // 破碎堆内存。
    // BufferManager类上公开的操作不是线程安全的。
    class BufferManager
    {
        /// <summary>
        /// 由缓冲池控制的总字节数
        /// </summary>
        private int m_numBytes;
        /// <summary>
        /// 由缓冲区管理器维护的底层字节数组
        /// </summary>
        private byte[] m_buffer;
        private Stack<int> m_freeIndexPool;
        private int m_currentIndex;
        private int m_bufferSize;

        public BufferManager(int totalBytes, int bufferSize)
        {
            m_numBytes = totalBytes;
            m_currentIndex = 0;
            m_bufferSize = bufferSize;
            m_freeIndexPool = new Stack<int>();
        }

        /// <summary>
        /// 分配缓冲池使用的缓冲空间
        /// </summary>
        public void InitBuffer()
        {
            //创建一个大的缓冲区并分割它
            //输出到每个SocketAsyncEventArg对象
            m_buffer = new byte[m_numBytes];
        }

        /// <summary>
        /// 从缓冲池中分配一个缓冲区给
        /// </summary>
        /// <param name="args">如果成功设置缓冲区则为true，否则为false</param>
        /// <returns></returns>
        public bool SetBuffer(SocketAsyncEventArgs args)
        {
            if (m_freeIndexPool.Count > 0)
            {
                args.SetBuffer(m_buffer, m_freeIndexPool.Pop(), m_bufferSize);
            }
            else
            {
                if ((m_numBytes - m_bufferSize) < m_currentIndex)
                {
                    return false;
                }
                args.SetBuffer(m_buffer, m_currentIndex, m_bufferSize);
                m_currentIndex += m_bufferSize;
            }
            return true;
        }

        /// <summary>
        /// 从SocketAsyncEventArg对象中移除缓冲区。
        /// 将缓冲区释放回缓冲池
        /// </summary>
        /// <param name="args"></param>
        public void FreeBuffer(SocketAsyncEventArgs args)
        {
            m_freeIndexPool.Push(args.Offset);
            args.SetBuffer(null, 0, 0);
        }
    }

}

