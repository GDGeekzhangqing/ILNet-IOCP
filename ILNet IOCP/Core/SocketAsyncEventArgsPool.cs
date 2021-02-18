using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace ILNet_IOCP.Core
{
    //表示可重用的SocketAsyncEventArgs对象的集合。
    class SocketAsyncEventArgsPool
    {
        Stack<SocketAsyncEventArgs> m_pool;

        /// <summary>
        /// 将对象池初始化为指定的大小
        ///“capacity”参数为最大值
        /// 池可以容纳的SocketAsyncEventArgs对象
        /// </summary>
        /// <param name="capacity"></param>
        public SocketAsyncEventArgsPool(int capacity)
        {
            m_pool = new Stack<SocketAsyncEventArgs>(capacity);
        }

        /// <summary>
        /// 向池中添加一个SocketAsyncEventArg实例 添加到池中
        /// </summary>
        /// <param name="item">SocketAsyncEventArgs实例</param>
        public void Push(SocketAsyncEventArgs item)
        {
            if (item == null) { throw new ArgumentNullException("添加到SocketAsyncEventArgsPool的项不能为空！"); }
            lock (m_pool)
            {
                m_pool.Push(item);
            }
        }

        /// <summary>
        /// 从池中移除SocketAsyncEventArgs实例
        /// 并返回从池中删除的对象
        /// </summary>
        /// <returns></returns>
        public SocketAsyncEventArgs Pop()
        {
            lock (m_pool)
            {
                return m_pool.Pop();
            }
        }

        /// <summary>
        /// 池中SocketAsyncEventArgs实例的个数
        /// </summary>
        public int Count
        {
            get { return m_pool.Count; }
        }
    }
}
