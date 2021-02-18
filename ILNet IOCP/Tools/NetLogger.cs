using System;
using System.Collections.Generic;
using System.Text;

namespace ILNet_IOCP.Tools
{
    public enum LogLevel
    {
        None = 0,// None
        Warn = 1,//Yellow
        Error = 2,//Red
        Info = 3//Green
    }

    public class NetLogger
    {
        public static bool log = true;
        public static Action<string, int> logCB = null;

        /// <summary>
        /// 打印日志
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="lv"></param>
        public static void LogMsg(string msg, LogLevel lv = LogLevel.None)
        {
            if (log != true)
            {
                return;
            }
            //添加时间戳
            msg = DateTime.Now.ToLongTimeString() + " >> " + msg;
            if (logCB != null)
            {
                logCB(msg, (int)lv);
            }
            else
            {
                if (lv == LogLevel.None)
                {
                    Console.WriteLine(msg);
                }
                else if (lv == LogLevel.Warn)
                {
                    //Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("//--------------------Warn--------------------//");
                    Console.WriteLine(msg);
                    //Console.ForegroundColor = ConsoleColor.Gray;
                }
                else if (lv == LogLevel.Error)
                {
                    //Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("//--------------------Error--------------------//");
                    Console.WriteLine(msg);
                    //Console.ForegroundColor = ConsoleColor.Gray;
                }
                else if (lv == LogLevel.Info)
                {
                    //Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("//--------------------Info--------------------//");
                    Console.WriteLine(msg);
                    //Console.ForegroundColor = ConsoleColor.Gray;
                }
                else
                {
                    //Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("//--------------------Error--------------------//");
                    Console.WriteLine(msg + " >> Unknow Log Type\n");
                    //Console.ForegroundColor = ConsoleColor.Gray;
                }
            }
        }

    }
}
