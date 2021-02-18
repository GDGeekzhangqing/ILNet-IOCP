using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace ILNet_IOCP.Core
{
    public class AnalysisMsg
    {
        public static byte[] PackNetMsg<T>(T msg) where T : NetMsg
        {
            return PackLenInfo(Serialize(msg));
        }

        public static byte[] PackLenInfo(byte[] data)
        {
            int len = data.Length;
            byte[] pkg = new byte[len + 4];
            byte[] head = BitConverter.GetBytes(len);
            head.CopyTo(pkg, 0);
            data.CopyTo(pkg, 4);
            return pkg;
        }

        public static byte[] Serialize<T>(T pkg) where T : NetMsg
        {
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(ms, pkg);
                ms.Seek(0, SeekOrigin.Begin);
                return ms.ToArray();
            }
        }

        public static T DeSerialize<T>(byte[] bs) where T : NetMsg
        {
            using (MemoryStream ms = new MemoryStream(bs))
            {
                BinaryFormatter bf = new BinaryFormatter();
                T pkg = (T)bf.Deserialize(ms);
                return pkg;
            }
        }
    }
}
