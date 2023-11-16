using System;
using System.Collections.Generic;
using System.Text;

namespace ConsoleTest
{
    public enum DeviceStatus
    {
        normal,
        warning,
        alarm
    }
    //存储在数据库中的数据
    public class DeviceData
    {
        public int ID { get; set; }
        public string DeviceID { get; set; }
        public string PartID { get; set; }
        public string PartName { get; set; }
        private int partAmountInDevice;
        public int PartAmountInDevice
        {
            get { return partAmountInDevice; }
            set { partAmountInDevice = value; ValueChanged?.Invoke(); }
        }
        public int Capacity { get; set; }

        public Action ValueChanged;

        public DeviceData()
        {

        }

    }
    //存放工作中设备的数据结构
    public class WorkingDevice
    {
        private DeviceData deviceUsed;
        public DeviceData DeviceUsed
        {
            get { return deviceUsed; }
            set
            {
                deviceUsed = value; if (DeviceUsed != null)
                {
                    UpdateStatus();
                    DeviceUsed.ValueChanged -= UpdateStatus;
                    DeviceUsed.ValueChanged += UpdateStatus;
                }
            }
        }

        private int completeAmount;
        public int CompleteAmount
        {
            get { return completeAmount; }
            set
            {
                completeAmount = value; if (DeviceUsed != null)
                {
                    DeviceUsed.PartAmountInDevice -= completeAmount;
                }
            }
        }
        public int FinallyCompleteAmount { get; set; }
        public int TargetAmount { get; set; }
        public double WarningRate { get; set; }
        public DeviceStatus Status { get; set; }

        public WorkingDevice(DeviceData deviceData, double warningRate = 0.2)
        {
            DeviceUsed = deviceData;
            CompleteAmount = 0;
            FinallyCompleteAmount = 0;
            WarningRate = warningRate;
            //DeviceUsed.ValueChanged += UpdateStatus;
            UpdateStatus();
        }

        public WorkingDevice(double warningRate = 0.2)
        {
            CompleteAmount = 0;
            FinallyCompleteAmount = 0;
            WarningRate = warningRate;
            UpdateStatus();
        }

        public void UpdateStatus()
        {
            if(DeviceUsed != null)
            {
                if (DeviceUsed.DeviceID == "null")
                {
                    Status = DeviceStatus.alarm;
                    return;
                }
                if (DeviceUsed.PartAmountInDevice >= WarningRate * DeviceUsed.Capacity)
                {
                    Status = DeviceStatus.normal;
                }
                else if (DeviceUsed.PartAmountInDevice > 0 && DeviceUsed.PartAmountInDevice < WarningRate * DeviceUsed.Capacity)
                {
                    Status = DeviceStatus.warning;
                }
                else
                {
                    Status = DeviceStatus.alarm;
                }
            }
        }

        public string ShowData()
        {
            return string.Format("设备ID：{0}  零件ID：{1}  零件代号：{2}  目标数量：{3}  最终完成数量：{4}  ",
                DeviceUsed.DeviceID, DeviceUsed.PartID, DeviceUsed.PartName, TargetAmount, FinallyCompleteAmount);
        }
    }
}
