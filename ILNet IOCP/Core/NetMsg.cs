using System;
using System.Collections.Generic;
using System.Text;

namespace ILNet_IOCP.Core
{
    [Serializable]
    public abstract class NetMsg
    {
        /// <summary>
        /// 序列
        /// </summary>
        public int seq;

        /// <summary>
        /// 指令
        /// </summary>
        public int cmd;

        /// <summary>
        /// 错误码
        /// </summary>
        public int err;

        /// <summary>
        /// 确认报文
        /// </summary>
        public int ack;

    }
}
