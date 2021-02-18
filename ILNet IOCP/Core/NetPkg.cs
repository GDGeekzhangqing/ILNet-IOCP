using System;
using System.Collections.Generic;
using System.Text;

namespace ILNet_IOCP.Core
{
    public class NetPkg
    {
        /// <summary>
        /// 包头长度
        /// </summary>
        public int headLen = 4;
        public byte[] headBuff = null;
        public int headIndex = 0;

        /// <summary>
        /// 数据包长度
        /// </summary>
        public int bodyLen = 0;
        public byte[] bodyBuff = null;
        public int bodyIndex = 0;

        public NetPkg()
        {
            headBuff = new byte[4];
        }

        /// <summary>
        /// 获取四个字节组成的int长度
        /// </summary>
        public void InitBodyBuff()
        {
            bodyLen = BitConverter.ToInt32(headBuff, 0);
            bodyBuff = new byte[bodyLen];
        }

        public void ResetData()
        {
            headIndex = 0;
            bodyLen = 0;
            bodyBuff = null;
            bodyIndex = 0;
        }

    }
}
