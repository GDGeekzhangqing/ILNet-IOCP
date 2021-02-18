using ILNet_IOCP.Tools;
using System;
using System.Collections.Generic;
using System.Text;

namespace ILNet_IOCP.Core
{
    public class AckMgr<T, R> : IDisposable where R : NetAckMsg, new()
    {

        /// <summary>
        /// 主定时器
        /// </summary>
        public System.Timers.Timer Checktimer;
        public System.Timers.Timer Sendtimer;

        private readonly object lockObj = new object();


        /// <summary>
        /// 发送心跳包事件
        /// </summary>
        public event Action<T> SendHearEvent;

        /// <summary>
        /// 心跳包超时事件
        /// </summary>
        public event Action<T> ConnectLostEvent;

        /// <summary>
        /// 每个会话对应一个心跳NetAckMsg
        /// </summary>
        private Dictionary<T, R> connectDic = new Dictionary<T, R>();

        public Dictionary<T, R> ConnectDic { get => connectDic; protected set => connectDic = value; }



        public virtual AckMgr<T, R> InitTimerEvent(Action<T> sendHearEvent, Action<T> connectLostEvent, double Checkinterval = 1000, double Sendinterval = 1000)
        {
            //这里是赋值每过多少秒执行一次事件
            Checktimer = new System.Timers.Timer(Checkinterval);
            Sendtimer = new System.Timers.Timer(Sendinterval);


            SendHearEvent = sendHearEvent;
            ConnectLostEvent = connectLostEvent;

            //定时执行事件
            Checktimer.Elapsed += (v, f) =>
            {
                //遍历每个会话回调一次检查心跳包
                CheckHeartBeat();
                //NetLogger.LogMsg("定时检测心跳包中...");
            };

            //定时执行事件
            Sendtimer.Elapsed += (v, f) =>
            {
                //遍历每个会话回调一次发送心跳包
                lock (lockObj)
                {
                    SendHeartBeat();
                    //NetLogger.LogMsg("定时发送心跳包...");
                }
            };

            return this;
        }

        public virtual AckMgr<T, R> StartTimer()
        {
            Checktimer.Start();
            Sendtimer.Start();
            NetLogger.LogMsg("开始心跳计时...");
            return this;
        }

        public virtual AckMgr<T, R> StopTimer()
        {
            Checktimer.Stop();
            Sendtimer.Stop();
            NetLogger.LogMsg("结束心跳计时...");
            return this;
        }


        /// <summary>
        /// 发送心跳包
        /// </summary>
        public virtual void SendHeartBeat()
        {
            lock (lockObj)
            {
                if (ConnectDic.Count > 0 && SendHearEvent != null)
                {
                    List<T> RemoveList = new List<T>();
                    foreach (KeyValuePair<T, R> item in ConnectDic)
                    {
                        SendHearEvent?.Invoke(item.Key);
                        //NetLogger.LogMsg("发送心跳包");
                    }
                }
            }
        }

        /// <summary>
        /// 检查心跳包
        /// </summary>
        public virtual void CheckHeartBeat()
        {
            lock (lockObj)
            {
                if (ConnectDic.Count > 0)
                {
                    List<T> RemoveList = new List<T>();
                    //NetLogger.LogMsg("当前conecntDic更新："+connectDic.Count);
                    foreach (KeyValuePair<T, R> item in ConnectDic)
                    {
                        //检查 心跳包超时 如果超时满次数就移除并回调事件
                        item.Value.CheckHeat();
                        if (item.Value.Lostcount >= item.Value.MaxLostcount)
                        {
                            RemoveList.Add(item.Key);
                        }
                        //对被移除元素进行指定操作
                        RemoveList.ForEach(remove =>
                        {
                            ConnectLostEvent(remove);
                            ConnectDic.Remove(remove);
                            NetLogger.LogMsg("移除当前客户端的心跳监听");
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 添加存储新的心跳包
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="heart"></param>
        /// <param name="maxlosttime"></param>
        /// <param name="maxlost"></param>
        public AckMgr<T, R> AddConnectDic(T obj, R heart = null, double maxlosttime = 2, int maxlost = 3)
        {
            heart = new R().InitMax<R>(maxlosttime, maxlost);

            lock (lockObj)
            {
                try
                {
                    ConnectDic.Add(obj, heart);
                }
                catch (Exception e)
                {
                    NetLogger.LogMsg("请指明心跳包的具体消息体:" + e);
                }
            }

            return this;
        }


        /// <summary>
        /// 移除心跳包
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public AckMgr<T, R> RemoveConnectDic(T obj)
        {
            lock (lockObj)
            {
                ConnectDic.Remove(obj);
            }

            return this;
        }

        /// <summary>
        /// 更新指定会话对应的心跳包
        /// </summary>
        /// <param name="obj"></param>
        public void UpdateOneHeat(T obj)
        {
            lock (lockObj)
            {
                ConnectDic[obj].UpdateHeat();
                NetLogger.LogMsg("更新指定会话对应的心跳包");
            }
        }

        /// <summary>
        /// 断开
        /// </summary>
        public void Dispose()
        {
            Checktimer.Dispose();
            Sendtimer.Dispose();
            NetLogger.LogMsg("断开心跳检测");
        }


    }
}
