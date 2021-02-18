using System;
using System.Collections.Generic;
using System.Text;

namespace ILNet_IOCP.Core
{
    public class NetAckMsg
    {
        /// <summary>
        /// 保存上一次心跳的时间
        /// </summary>
        public long lastHeartTime;

        /// <summary>
        /// 超时次数
        /// </summary>
        public int Lostcount;

        /// <summary>
        /// 心跳包最大超时多少次
        /// </summary>
        public int MaxLostcount;

        /// <summary>
        /// 超时多久算一次
        /// </summary>
        public double MaxLostTime;

        /// <summary>
        /// 计算时间差的
        /// </summary>
        public long NowTimeSpan => Convert.ToInt64((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalSeconds);

        /// <summary>
        /// 检查心跳包延时
        /// </summary>
        public virtual void CheckHeat()
        {
            if (Math.Abs(lastHeartTime - this.NowTimeSpan) > MaxLostTime)
            {
                lastHeartTime = this.NowTimeSpan;
                Lostcount++;
            }
        }


        /// <summary>
        /// 更新心跳包
        /// </summary>
        public virtual void UpdateHeat()
        {
            lastHeartTime = this.NowTimeSpan;
            Lostcount = 0;
        }

        /// <summary>
        /// 初始化心跳包
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="maxlosttime"></param>
        /// <param name="maxlost"></param>
        /// <returns></returns>
        public virtual T InitMax<T>(double maxlosttime = 2, int maxlost = 3) where T : NetAckMsg
        {
            MaxLostTime = maxlosttime;
            MaxLostcount = maxlost;
            //第一次赋值肯定要刷新
            lastHeartTime = this.NowTimeSpan;
            return this as T;
        }


        /// <summary>
        /// 初始化心跳包
        /// </summary>
        /// <param name="maxlosttime"></param>
        /// <param name="maxlost"></param>
        public virtual void InitMax(double maxlosttime = 2, int maxlost = 3)
        {
            MaxLostTime = maxlosttime;
            MaxLostcount = maxlost;
            //第一次赋值肯定要刷新
            lastHeartTime = this.NowTimeSpan;
        }

    }
}
