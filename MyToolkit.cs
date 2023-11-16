using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LitJson;

namespace MyToolkit
{
    public class ConnectionToolkit
    {
        public class GetIPAddress
        {
            public static List<IPAddress> GetIPV4AddressList()
            {
                List<IPAddress> ipAddressList = new List<IPAddress>();
                string name = Dns.GetHostName();
                IPAddress[] ipadrlist = Dns.GetHostAddresses(name);
                foreach (IPAddress ipa in ipadrlist)
                {
                    if (ipa.AddressFamily == AddressFamily.InterNetwork)
                        ipAddressList.Add(ipa);
                }
                return ipAddressList;
            }

            public static IPAddress GetTargetIPV4Address(string partialIP)
            {
                string[] targetPart = partialIP.Split('.');
                foreach (var item in GetIPV4AddressList())
                {
                    string[] part = item.ToString().Split('.');
                    for (int i = 0; i < targetPart.Length; i++)
                    {
                        if (part[i].Equals(targetPart[i]))
                        {
                            return item;
                        }
                    }
                }
                return null;
            }
        }

        public class SocketConnection
        {
            //基本参数
            public Socket SocketItem { get; set; }
            public byte[] DataCache { get; set; }
            public byte[] SendByte { get; set; }
            //服务端所需参数
            public Dictionary<string, Socket> ClientDic { get; set; }
            public IPAddress IP { get; set; }
            public int Port { get; set; }
            public IPEndPoint IPEndPoint { get; set; }
            //数据收发更新委托
            public Action ClientListUpdate;
            public Action<Socket, byte[]> ReceiveFromClient;
            public Action<byte[]> ReceiveFromServer;

            public SocketConnection(int byteLength = 2048)
            {
                SocketItem = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                DataCache = new byte[byteLength];
                SendByte = new byte[byteLength];
            }

            public SocketConnection(string ip, int port, int byteLength = 2048)
            {
                SocketItem = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                DataCache = new byte[byteLength];
                SendByte = new byte[byteLength];

                ClientDic = new Dictionary<string, Socket>();

                IPAddress.TryParse(ip, out IPAddress iPAddress);

                this.IP = iPAddress;
                this.Port = port;
                this.IPEndPoint = new IPEndPoint(IP, Port);
            }

            private byte[] GetByteArray(byte[] byteArr, int satrt, int length)//截取特定长度的字节数组
            {
                byte[] res = new byte[length];
                if (byteArr != null && byteArr.Length >= length)
                {
                    for (int i = 0; i < length; i++)
                    {
                        res[i] = byteArr[i + satrt];
                    }
                }
                return res;
            }

            #region 客户端
            public bool Connection(string IP, int Port, out string error)
            {
                try
                {
                    IPAddress.TryParse(IP, out IPAddress iPAddress);
                    IAsyncResult result = SocketItem.BeginConnect(iPAddress, Port, null, null);
                    var isConnect = result.AsyncWaitHandle.WaitOne(5000, true);
                    //SocketItem.Connect(iPAddress, Port);
                    if (!isConnect)
                    {
                        error = "PLC连接超时。";
                        SocketItem.Close();
                        return false;
                    }
                    SocketItem.EndConnect(result);
                    Task.Run(() => this.ReceiveData());
                }
                catch (Exception e)
                {
                    error = e.Message;
                    return false;
                }
                error = "";
                return true;
            }

            public bool Disconnection()
            {
                try
                {
                    SocketItem.Shutdown(SocketShutdown.Both);
                    SocketItem.Close();
                }
                catch (Exception)
                {
                    return false;
                }
                return true;
            }

            public void ReceiveData()
            {
                while (true)
                {
                    int length = -1;
                    try
                    {
                        length = SocketItem.Receive(DataCache);
                    }
                    catch (SocketException e)
                    {
                        if (e.ErrorCode == 10004) return;//数据接收阻塞被取消
                    }
                    catch (Exception)
                    {
                        return;
                    }
                    if (length > 0)
                    {
                        byte[] result = GetByteArray(DataCache, 0, length);//读取plc通信区的数据——写入区和数据读取区并截取
                        ReceiveFromServer?.Invoke(result);//接收数据
                    }
                    else
                    {
                        SocketItem.Shutdown(SocketShutdown.Both);
                        SocketItem.Close();
                        return;
                    }
                    System.Threading.Thread.Sleep(10);
                }
            }
            #endregion

            #region 服务端
            public bool StartListening()
            {
                try
                {
                    SocketItem.Bind(IPEndPoint);
                    SocketItem.Listen(200);
                    ThreadPool.QueueUserWorkItem(new WaitCallback(StartAcceptClient), SocketItem);
                }
                catch (Exception)
                {
                    return false;
                }
                return true;
            }

            public bool StopListening()
            {
                try
                {
                    SocketItem.Close();
                    ClientDic.Clear();
                }
                catch (Exception)
                {
                    return false;
                }
                return true;
            }

            public void StartAcceptClient(object server)//会自动在其他线程上启动ReceiveData（client）方法
            {
                var socketServer = (Socket)server;
                while (true)
                {
                    try
                    {
                        Socket socketClient = socketServer.Accept();
                        ClientDic.Add(socketClient.RemoteEndPoint.ToString(), socketClient);
                        ClientListUpdate?.Invoke();//更新客户端列表//更新客户端列表
                        ThreadPool.QueueUserWorkItem(new WaitCallback(ReceiveData), socketClient);
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
            }

            public void ReceiveData(object client)//服务端消息监听
            {
                var socketClient = (Socket)client;
                while (true)
                {
                    int length = -1;
                    try
                    {
                        length = socketClient.Receive(DataCache);//plc设置的通信数据区字节数
                    }
                    catch (Exception)
                    {
                        ClientDic.Remove(socketClient.RemoteEndPoint.ToString());
                        socketClient.Shutdown(SocketShutdown.Both);
                        socketClient.Close(100);
                        ClientListUpdate?.Invoke();//更新客户端列表
                        return;
                    }
                    if (length > 0)
                    {
                        byte[] result = GetByteArray(DataCache, 0, length);//读取plc通信区的数据——写入区和数据读取区并截取
                        ReceiveFromClient?.Invoke(socketClient, result);//服务端应答委托//服务端应答委托
                    }
                    else
                    {
                        ClientDic.Remove(socketClient.RemoteEndPoint.ToString());
                        socketClient.Shutdown(SocketShutdown.Both);
                        socketClient.Close();
                        ClientListUpdate?.Invoke();//更新客户端列表
                        return;
                    }
                    Thread.Sleep(10);
                }
            }
            #endregion

            public void SendUTF8(byte[] data)
            {
                //float f = 0.1f;
                //byte[] set1 = BitConverter.GetBytes(f);
                //SendByte[0] = set1[3];
                //SendByte[1] = set1[2];
                //SendByte[2] = set1[1];
                //SendByte[3] = set1[0];
                SendByte = Encoding.UTF8.GetBytes(data.ToString());
                SocketItem.Send(SendByte);//覆盖plc写入区的数据
            }
        }

        //public class SerialPortConnection
        //{
        //    public readonly SerialPort MySerialPort;//串口对象
        //    public Action<string> ReceivedString;
        //    public Action<byte[]> ReceivedByte;
        //    //public delegate void ShowMsgDelegate(string msg);
        //    //public ShowMsgDelegate SendMsg;

        //    int receivedByteCount = 0;
        //    byte reveivedByte;
        //    readonly byte[] dataCache;

        //    public SerialPortConnection(int byteLength = 1024)
        //    {
        //        MySerialPort = new SerialPort();
        //        dataCache = new byte[byteLength];
        //    }

        //    private byte[] GetByteArray(byte[] byteArr, int satrt, int length)//截取特定长度的字节数组
        //    {
        //        byte[] res = new byte[length];
        //        if (byteArr != null && byteArr.Length >= length)
        //        {
        //            for (int i = 0; i < length; i++)
        //            {
        //                res[i] = byteArr[i + satrt];
        //            }
        //        }
        //        return res;
        //    }

        //    public bool OpenMySerialPort(int iBaudRate, string portName, int dataBits, Parity iParity, StopBits iStopBits)
        //    {
        //        try
        //        {
        //            if (MySerialPort.IsOpen)
        //            {
        //                MySerialPort.Close();
        //            }
        //            MySerialPort.BaudRate = iBaudRate;
        //            MySerialPort.PortName = portName;
        //            MySerialPort.DataBits = dataBits;
        //            MySerialPort.Parity = iParity;
        //            MySerialPort.StopBits = iStopBits;

        //            MySerialPort.ReceivedBytesThreshold = 1;
        //            MySerialPort.DataReceived += MySerialPortDataReceived;//绑定接收事件

        //            MySerialPort.Open();
        //            return true;
        //        }
        //        catch (Exception)
        //        {
        //            return false;
        //        }
        //    }

        //    public bool OpenMySerialPort(int iBaudRate, string portName)
        //    {
        //        try
        //        {
        //            if (MySerialPort.IsOpen)
        //            {
        //                MySerialPort.Close();
        //            }
        //            MySerialPort.BaudRate = iBaudRate;
        //            MySerialPort.PortName = portName;
        //            MySerialPort.DataBits = 8;
        //            MySerialPort.Parity = Parity.None;
        //            MySerialPort.StopBits = StopBits.One;

        //            MySerialPort.ReceivedBytesThreshold = 1;//缓存中数据多少个时才触发DataReceived事件，默认为1
        //            MySerialPort.DataReceived += MySerialPortDataReceived;//绑定接收事件

        //            MySerialPort.Open();
        //            return true;
        //        }
        //        catch (Exception)
        //        {
        //            return false;
        //        }
        //    }

        //    private void MySerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)//接收事件，接收到数据时触发
        //    {
        //        //接收数据
        //        receivedByteCount = 0;
        //        while (MySerialPort.BytesToRead > 0)
        //        {
        //            reveivedByte = (byte)MySerialPort.ReadByte();
        //            dataCache[receivedByteCount] = reveivedByte;
        //            receivedByteCount++;

        //            if (receivedByteCount >= 1024)
        //            {
        //                receivedByteCount = 0;
        //                MySerialPort.DiscardInBuffer();
        //                return;
        //            }
        //        }

        //        if (receivedByteCount > 0)
        //        {
        //            byte[] b = GetByteArray(dataCache, 0, receivedByteCount);
        //            ReceivedString?.Invoke(Encoding.ASCII.GetString(b));//通过委托传出接收到的数据
        //            ReceivedByte?.Invoke(b);
        //            //SendMsg(Encoding.ASCII.GetString(b));//调用委托，传出接收到的数据
        //        }
        //    }

        //    public void SendMessage(string msg)
        //    {
        //        MySerialPort.Write(msg);
        //    }

        //    public bool CloseMySerialPort()
        //    {
        //        if (MySerialPort.IsOpen)
        //        {
        //            MySerialPort.Close();
        //        }
        //        return true;
        //    }
        //}
    }

    public class DataConverter
    {
        //16进制字符串转字节数组
        public static byte[] HexStringToBytes(string hexString)
        {
            hexString = hexString.Trim();
            if ((hexString.Length % 2) != 0)
                hexString += " ";
            byte[] bytes = new byte[hexString.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hexString.Substring(2 * i, 2).Trim(), 16);
            }
            return bytes;
        }
        //字节数组转16进制字符串
        public static string BytesToHexString(byte[] bytes)
        {
            string hexString = "";
            if (bytes != null)
            {
                for (int i = 0;i < bytes.Length; i++)
                {
                    hexString += bytes[i].ToString("X2");
                }
            }
            return hexString;
        }
        //16进制字符串转整数字符串
        public static string HexStringToIntString(string hexString)
        {
            if (int.TryParse(hexString, System.Globalization.NumberStyles.HexNumber, null, out int result))
            {
                return result.ToString();
            }
            return string.Empty;
        }
        //16进制字符串转整数
        public static int HexStringToInt(string hexString)
        {
            if (int.TryParse(hexString, System.Globalization.NumberStyles.HexNumber, null, out int result))
            {
                return result;
            }
            return -1;
        }

        public static string IntStringToTwoDigitsHexString(string intString)
        {
            if (int.TryParse(intString, out int result))
            {
                if (result < 0 || result > 255)
                    return "";
                return result.ToString("X2");
            }
            return "";
        }

        public static string IntStringToFourDigitsHexString(string intString)
        {
            if (int.TryParse(intString, out int result))
            {
                if (result < 0 || result > 65535)
                    return "";
                return result.ToString("X4");
            }
            return "";
        }

        public static int TwoBytesToInt(byte[] bytes, bool isLittleEndian = false)
        {
            if (isLittleEndian)
            {
                return BitConverter.ToInt16(bytes, 0);
            }
            else
            {
                Array.Reverse(bytes);
                return BitConverter.ToInt16(bytes, 0);
            }
        }

        public static int FourBytesToInt(byte[] bytes, bool isLittleEndian = false)
        {
            if (isLittleEndian)
            {
                return BitConverter.ToInt32(bytes, 0);
            }
            else
            {
                Array.Reverse(bytes);
                return BitConverter.ToInt32(bytes, 0);
            }
        }
        //高低位互换
        public static string ToLowHigh(string hexString)
        {
            byte[] bytes = HexStringToBytes(hexString);
            Array.Reverse(bytes);
            return BytesToHexString(bytes);
        }
    }

    public class FileManager
    {
        public static string GetLocalAppPath(string fileName)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), fileName);
        }

        public static void AppendStreamString(string path, string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            FileStream file = new FileStream(path, FileMode.Append);
            file.Write(data, 0, data.Length);
            file.Flush();
            file.Close();
            file.Dispose();
        }

        public static void CreatStreamString(string path, string message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            FileStream file = new FileStream(path, FileMode.Create);
            file.Write(data, 0, data.Length);
            file.Flush();
            file.Close();
            file.Dispose();
        }

        public static void AppendLog(string path, string message)
        {
            string log = DateTime.Now.ToString("yyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine;
            AppendStreamString(path, log);
        }
    }

    public class JsonManager
    {
        public static void SaveJsonString(string path, string fileName, object data)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            path = path + "\\" + fileName + ".json";

            string jsonString = JsonMapper.ToJson(data);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonString);
            FileStream file = new FileStream(path, FileMode.Create);
            file.Write(jsonBytes, 0, jsonBytes.Length);//写入
            file.Flush();
            file.Close();
        }

        public static T ReadJsonString<T>(string path, string fileName)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                path = path + "\\" + fileName + ".json";

                FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                StreamReader stream = new StreamReader(file);
                T jsonData = JsonMapper.ToObject<T>(stream.ReadToEnd());
                file.Flush();
                file.Close();
                //T jsonData = JsonMapper.ToObject<T>(File.ReadAllText(path));
                return jsonData;
            }
            catch (Exception)
            {

            }
            return default(T);
        }

        public static JsonData ReadSimpleJsonString(string path)
        {
            JsonData jsonData = JsonMapper.ToObject(File.ReadAllText(path));
            return jsonData;
        }
    }

    public class ByteArrayToolkit
    {
        //==========静态函数==========//
        #region 插入与拼接
        public static byte[] SpliceBytes(byte[] beginningArray, byte[] endingArray)
        {
            byte[] bytes = new byte[beginningArray.Length + endingArray.Length];
            beginningArray.CopyTo(bytes, 0);
            endingArray.CopyTo(bytes, beginningArray.Length);
            return bytes;
        }
        /// <summary>
        /// 在insertIndex处插入一个字节数组
        /// </summary>
        /// <param name="sourceBytes">被插入的字节数组</param>
        /// <param name="insertBytes">要插入的字节数组</param>
        /// <param name="insertIndex">要插入的数组索引处</param>
        /// <returns>插入后的字节数组</returns>
        public static byte[] InsertBytes(byte[] sourceBytes, byte[] insertBytes, int insertIndex)
        {
            byte[] bytes = new byte[sourceBytes.Length + insertBytes.Length];
            if (insertIndex < 0) return sourceBytes;
            if (insertIndex > sourceBytes.Length) return sourceBytes;
            Array.ConstrainedCopy(sourceBytes, 0, bytes, 0, insertIndex);
            Array.ConstrainedCopy(insertBytes, 0, bytes, insertIndex, insertBytes.Length);
            Array.ConstrainedCopy(sourceBytes, insertIndex, bytes, insertIndex + insertBytes.Length, sourceBytes.Length - insertIndex);
            return bytes;
        }
        /// <summary>
        /// 在数组中的标记字节后插入一个数组  与插入的数组长度还无关联
        /// </summary>
        /// <param name="sourceArray">源数组</param>
        /// <param name="mark">标记字节</param>
        /// <param name="insertArray">要插入的数组</param>
        /// <returns>插入后的字节数组</returns>
        public static byte[] InsertBytesBackOfMark(byte[] sourceArray, byte mark, byte[] insertArray)
        {
            List<int> index = new List<int>();
            for (int i = 0; i < sourceArray.Length; i++)
            {
                if (sourceArray[i] == mark)
                {
                    index.Add(i);
                }
            }
            if (index.Count == 0) return sourceArray;
            return LoopInsert(sourceArray, insertArray, index, index.Count);
        }

        public static byte[] LoopInsert(byte[] sourceArray, byte[] insertArray, List<int> index, int count)
        {
            if (count == 1)
            {
                return InsertBytes(sourceArray, insertArray, index[count - 1] + count);
            }
            else
            {
                return InsertBytes(LoopInsert(sourceArray, insertArray, index, count - 1), insertArray, index[count - 1] + count + (insertArray.Length - 1) * (count - 1));
            }
        }
        #endregion

        #region 去除与剪切
        /// <summary>
        /// 从源字节数组中剪裁出指定首尾索引中间的字节数组
        /// </summary>
        /// <param name="sourceBytes">源字节数组</param>
        /// <param name="beginningIndex">要开始剪裁的字节索引</param>
        /// <param name="endingIndex">要结束剪裁的字节索引</param>
        /// <returns>包括首尾索引字节的字节数组</returns>
        public static byte[] CutBytes(byte[] sourceBytes, int beginningIndex, int endingIndex)
        {
            if (beginningIndex < 0 || endingIndex < 0) return sourceBytes;
            if (beginningIndex >= endingIndex) return sourceBytes;
            byte[] bytes = new byte[endingIndex - beginningIndex + 1];
            Array.ConstrainedCopy(sourceBytes, beginningIndex, bytes, 0, bytes.Length);
            return bytes;
        }

        public static byte[] CutBytesByLength(byte[] sourceBytes, int beginningIndex, int dataLength)
        {
            if (beginningIndex < 0 || dataLength < 0) return sourceBytes;
            if (sourceBytes.Length - beginningIndex < dataLength) return sourceBytes;
            byte[] bytes = new byte[dataLength];
            Array.ConstrainedCopy(sourceBytes, beginningIndex, bytes, 0, dataLength);
            return bytes;
        }
        /// <summary>
        /// 去除指定索引处的字节
        /// </summary>
        /// <param name="sourceBytes"></param>
        /// <param name="redundantIndex"></param>
        /// <returns></returns>
        public static byte[] RemoveByte(byte[] sourceBytes, int redundantIndex)
        {
            if (redundantIndex > sourceBytes.Length - 1) return sourceBytes;
            byte[] bytes = new byte[sourceBytes.Length - 1];
            Array.ConstrainedCopy(sourceBytes, 0, bytes, 0, redundantIndex);
            Array.ConstrainedCopy(sourceBytes, redundantIndex + 1, bytes, redundantIndex, bytes.Length - redundantIndex);
            return bytes;
        }
        /// <summary>
        /// 移除特殊字节后的标记字节
        /// </summary>
        /// <param name="sourceArray">源字节数组</param>
        /// <param name="specialByte">特殊字节</param>
        /// <param name="mark">标记字节</param>
        public static byte[] RemoveMark(byte[] sourceArray, byte specialByte, byte mark)
        {
            List<int> index = new List<int>(sourceArray.Length);
            for (int i = 0; i < sourceArray.Length; i++)
            {
                if (sourceArray[i] == specialByte)
                {
                    if (i < sourceArray.Length - 1)
                    {
                        if (sourceArray[i + 1] == mark)
                            index.Add(i + 1);
                    }
                }
            }
            if (index.Count == 0) return sourceArray;
            return LoopRemove(sourceArray, index, index.Count);
        }

        public static byte[] LoopRemove(byte[] sourceArray, List<int> index, int count)
        {
            if (count == 1)
            {
                return RemoveByte(sourceArray, index[index.Count - count]);//从后向前减，减少代码复杂度
            }
            else
            {
                return RemoveByte(LoopRemove(sourceArray, index, count - 1), index[index.Count - count]);
            }
        }
        #endregion

        #region 检测
        /// <summary>
        /// 检查字节数组中标记数组的数量
        /// </summary>
        /// <param name="sourceArray">源字节数组</param>
        /// <param name="frameMark">需要检查的标记字节数组</param>
        /// <returns>标记的数量</returns>
        public static int CheckFrameMarkCount(byte[] sourceArray, byte[] frameMark)
        {
            if (sourceArray.Length == 0) return 0;
            if (sourceArray.Length < frameMark.Length) return 0;
            int count = 0;
            for (int i = 0; i < sourceArray.Length; i++)
            {
                if (sourceArray[i] == frameMark[0])
                {
                    //如果剩余长度已经小于标记字节数组的长度，直接返回
                    if (frameMark.Length > sourceArray.Length - i) return count;
                    for (int j = 0; j < frameMark.Length; j++)
                    {
                        if (sourceArray[i + j] != frameMark[j])
                        {
                            break;
                        }
                        else
                        {
                            if (j == frameMark.Length - 1)
                            {
                                i += j;
                                count++;
                            }
                        }
                    }
                }
            }
            return count;
        }
        //得到标记字节数组的第一个位置索引（头尾索引）
        public static void FindPackage(byte[] sourceArray, byte[] frameMark, out int head, out int tail)
        {
            head = -1;
            tail = -1;
            for (int i = 0; i < sourceArray.Length; i++)
            {
                if (sourceArray[i] == frameMark[0])
                {
                    for (int j = 0; j < frameMark.Length; j++)
                    {
                        if (head == -1)
                        {
                            if (i + j >= sourceArray.Length)
                            {
                                head = -1;
                                break;
                            }
                            if (sourceArray[i + j] != frameMark[j])
                            {
                                head = -1;
                                break;
                            }
                            else
                            {
                                if (j == frameMark.Length - 1)
                                    head = i;
                            }
                        }
                        else
                        {
                            if (i + j >= sourceArray.Length)
                            {
                                tail = -1;
                                break;
                            }
                            if (sourceArray[i + j] != frameMark[j])
                            {
                                tail = -1;
                                break;
                            }
                            else
                            {
                                if (j == frameMark.Length - 1)
                                {
                                    if (i - head < 2)
                                    {
                                        tail = -1;
                                    }
                                    else
                                    {
                                        tail = i;
                                    }
                                }
                            }
                        }
                    }
                }
                if (head != -1 && tail != -1) break;
            }
        }
        //得到数组开头是标记字节的索引位置（头尾索引）
        public static void CheckPackage(byte[] sourceArray, byte[] frameMark, out int head, out int tail)
        {
            head = -1;
            tail = -1;
            if (frameMark.Length > sourceArray.Length) return;
            for (int i = 0; i < sourceArray.Length; i++)
            {
                if (i < frameMark.Length - 1)
                {
                    for (int j = 0; j < frameMark.Length; j++)
                    {
                        if (i + j >= sourceArray.Length)
                        {
                            head = -1;
                            break;
                        }
                        if (head == -1)
                        {
                            if (sourceArray[i + j] != frameMark[j])
                            {
                                head = -1;
                                break;
                            }
                            else
                            {
                                if (j == frameMark.Length - 1)
                                    head = i;
                                if (head != 0)
                                {
                                    head = -1;
                                }
                            }
                        }
                    }
                }
                else
                {
                    for (int j = 0; j < frameMark.Length; j++)
                    {
                        if (i + j >= sourceArray.Length)
                        {
                            tail = -1;
                            break;
                        }
                        if (tail == -1)
                        {
                            if (sourceArray[i + j] != frameMark[j])
                            {
                                tail = -1;
                                break;
                            }
                            else
                            {
                                if (j == frameMark.Length - 1)
                                {
                                    tail = i;
                                }
                            }
                        }
                    }
                }
                if (tail != -1) break;
            }
        }
        //比较两个数组是否相等
        public static bool CheckEquals(byte[]b1,byte[] b2)
        {
            if (b1 == null || b2 == null) return false;
            if (b1.Length != b2.Length) return false;
            for (int i = 0; i < b1.Length; i++)
            {
                if (b1[i] != b2[i]) return false;
            }
            return true;
        }
        #endregion
    }

    public class BytesReceiveTool
    {
        public int FrameTagLength = 2;
        public int PackageLength = 0;
        public byte[] DataCache = new byte[1024];
        public Action<byte[]> ReceiveBytes;
        //==========非静态函数==========//

        public BytesReceiveTool()
        {

        }

        public void DataReceive(byte[] receivedData)
        {
            if (receivedData.Length == 0) return;
            Array.Copy(receivedData, 0, DataCache, PackageLength, receivedData.Length);
            //下一次接收的起始位置以及现有的数据长度
            PackageLength += receivedData.Length;

            //检测数据包
            ByteArrayToolkit.CheckPackage(DataCache, new byte[2] { 0x7F, 0x7F }, out int head, out int tail);
            //无帧头无帧尾,返回继续拼接
            if (head == -1 && tail == -1) return;
            //有帧头无帧尾,返回继续拼接
            if (head == 0 && tail == -1) return;
            //无帧头有帧尾
            if (head == -1 && tail != -1)
            {
                //将提取的数据消除，将后面的数据前置
                ClearDataCache(tail + FrameTagLength);
                //重新计算缓存区字节长度
                PackageLength -= (tail + FrameTagLength);
            }
            //有帧头帧尾
            if (head == 0 && tail != -1)
            {
                //拼接好的数据包要放入的字节数组
                byte[] data = new byte[tail + FrameTagLength];
                //将缓存中的数据拷贝到字节数组中
                Array.Copy(DataCache, 0, data, 0, tail + FrameTagLength);
                //传出数据
                ReceiveBytes?.Invoke(data);
                //将提取的数据消除，将后面的数据前置
                ClearDataCache(tail + FrameTagLength);
                //重新计算缓存区字节长度
                PackageLength -= (tail + FrameTagLength);
            }

            //再次检测数据包（防止粘包）
            ByteArrayToolkit.CheckPackage(DataCache, new byte[2] { 0x7F, 0x7F }, out int head2, out int tail2);
            //无帧头无帧尾,返回继续拼接
            if (head2 == -1 && tail2 == -1) return;
            //有帧头无帧尾,返回继续拼接
            if (head2 == 0 && tail2 == -1) return;
            //有帧头帧尾
            if (head2 == 0 && tail2 != -1)
            {
                //拼接好的数据包要放入的字节数组
                byte[] data = new byte[tail2 + FrameTagLength];
                //将缓存中的数据拷贝到字节数组中
                Array.Copy(DataCache, 0, data, 0, tail2 + FrameTagLength);
                //传出数据
                ReceiveBytes?.Invoke(data);
                //将提取的数据消除，将后面的数据前置
                ClearDataCache(tail2 + FrameTagLength);
                //重新计算缓存区字节长度
                PackageLength -= (tail2 + FrameTagLength);
            }

        }
        /// <summary>
        /// 将指定长度的数据清除，并用后面的数据覆盖
        /// </summary>
        /// <param name="clearLength">清除数据的长度</param>
        public void ClearDataCache(int clearLength)
        {
            //byte[] tempData = new byte[clearLength];
            for (int i = 0; i < DataCache.Length - clearLength; i++)
            {
                DataCache[i] = DataCache[clearLength + i];
            }
        }
    }

    public class FinsTCPToolkit
    {
        /// <summary>
        /// Fins协议握手指令
        /// </summary>
        /// <param name="localAddress">本地IP最后一段</param>
        /// <returns>所需16进制字符串握手指令</returns>
        public static string HandshakeString(string localAddress)
        {
            int.TryParse(localAddress, out int address);
            if (address >= 0 && address <= 255)
            {
                return "46494E53" + "0000000C" + "00000000" + "00000000" + "000000" + address.ToString("X2");
            }
            return "";
        }

        /// <summary>
        /// Fins协议读取PLC指定内存的数据
        /// </summary>
        /// <param name="remoteAddress">PLCIP最后一段地址，16进制，1字节</param>
        /// <param name="localAddress">本地IP最后一段地址，16进制，1字节</param>
        /// <param name="memoryArea">PLC内存地址代码，16进制，1字节</param>
        /// <param name="startAddress">读取数据起始地址，容量为16进制2字节</param>
        /// <param name="dataLength">读取数据长度，16进制，2字节</param>
        /// <returns>所需16进制字符串读取指令</returns>
        public static string ReadString(string remoteAddress, string localAddress, string memoryArea, int startAddress, string dataLength)
        {
            int.TryParse(localAddress, out int local);
            int.TryParse(remoteAddress, out int remote);
            if (local >= 0 && local <= 255 && remote >= 0 && remote <= 255)
                return "46494E53" + "0000001A" + "00000002" + "00000000" + "80" + "0002" +
                        "00" + remoteAddress + "00" + "00" + localAddress + "00" +
                        "FF0101" + memoryArea + startAddress.ToString("X4") + "00" + dataLength;
            return "";
        }

        /// <summary>
        /// Fins协议写入PLC指定内存数据
        /// </summary>
        /// <param name="remoteAddress">PLCIP最后一段地址，16进制，1字节</param>
        /// <param name="localAddress">本地IP最后一段地址，16进制，1字节</param>
        /// <param name="memoryArea">PLC内存地址代码，16进制，1字节</param>
        /// <param name="startAddress">写入数据起始地址，容量为16进制2字节</param>
        /// <param name="dataLength">写入数据长度，容量为16进制2字节</param>
        /// <param name="data">写入的数据，16进制，2字节*dataLength</param>
        /// <returns>所需16进制字符串写入指令</returns>
        public static string WriteString(string remoteAddress, string localAddress, string memoryArea, int startAddress, int dataLength, string data)
        {
            int codeLength = 0x0000001A + dataLength * 2;
            int.TryParse(localAddress, out int local);
            int.TryParse(remoteAddress, out int remote);
            if (local >= 0 && local <= 255 && remote >= 0 && remote <= 255)
                return "46494E53" + codeLength.ToString("X8") + "00000002" + "00000000" + "80" + "0002" +
                    "00" + remoteAddress + "00" + "00" + localAddress + "00" +
                    "FF0102" + memoryArea + startAddress.ToString("X4") + "00" + dataLength.ToString("X4") + data;
            return "";
        }
    }

    public class CRC16
    {
        //High-Order Byte Table
        /* Table of CRC values for high–order byte */
        static readonly byte[] auchCRCHi = new byte[256] {
    0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81,
    0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0,
    0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01,
    0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41,
    0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81,
    0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0,
    0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01,
    0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40,
    0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81,
    0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0,
    0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01,
    0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41,
    0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81,
    0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0,
    0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01,
    0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81, 0x40, 0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41,
    0x00, 0xC1, 0x81, 0x40, 0x01, 0xC0, 0x80, 0x41, 0x01, 0xC0, 0x80, 0x41, 0x00, 0xC1, 0x81,
    0x40 };
        //Low-Order Byte Table
        /* Table of CRC values for low–order byte */
        static readonly byte[] auchCRCLo = new byte[256]{
    0x00, 0xC0, 0xC1, 0x01, 0xC3, 0x03, 0x02, 0xC2, 0xC6, 0x06, 0x07, 0xC7, 0x05, 0xC5, 0xC4,
    0x04, 0xCC, 0x0C, 0x0D, 0xCD, 0x0F, 0xCF, 0xCE, 0x0E, 0x0A, 0xCA, 0xCB, 0x0B, 0xC9, 0x09,
    0x08, 0xC8, 0xD8, 0x18, 0x19, 0xD9, 0x1B, 0xDB, 0xDA, 0x1A, 0x1E, 0xDE, 0xDF, 0x1F, 0xDD,
    0x1D, 0x1C, 0xDC, 0x14, 0xD4, 0xD5, 0x15, 0xD7, 0x17, 0x16, 0xD6, 0xD2, 0x12, 0x13, 0xD3,
    0x11, 0xD1, 0xD0, 0x10, 0xF0, 0x30, 0x31, 0xF1, 0x33, 0xF3, 0xF2, 0x32, 0x36, 0xF6, 0xF7,
    0x37, 0xF5, 0x35, 0x34, 0xF4, 0x3C, 0xFC, 0xFD, 0x3D, 0xFF, 0x3F, 0x3E, 0xFE, 0xFA, 0x3A,
    0x3B, 0xFB, 0x39, 0xF9, 0xF8, 0x38, 0x28, 0xE8, 0xE9, 0x29, 0xEB, 0x2B, 0x2A, 0xEA, 0xEE,
    0x2E, 0x2F, 0xEF, 0x2D, 0xED, 0xEC, 0x2C, 0xE4, 0x24, 0x25, 0xE5, 0x27, 0xE7, 0xE6, 0x26,
    0x22, 0xE2, 0xE3, 0x23, 0xE1, 0x21, 0x20, 0xE0, 0xA0, 0x60, 0x61, 0xA1, 0x63, 0xA3, 0xA2,
    0x62, 0x66, 0xA6, 0xA7, 0x67, 0xA5, 0x65, 0x64, 0xA4, 0x6C, 0xAC, 0xAD, 0x6D, 0xAF, 0x6F,
    0x6E, 0xAE, 0xAA, 0x6A, 0x6B, 0xAB, 0x69, 0xA9, 0xA8, 0x68, 0x78, 0xB8, 0xB9, 0x79, 0xBB,
    0x7B, 0x7A, 0xBA, 0xBE, 0x7E, 0x7F, 0xBF, 0x7D, 0xBD, 0xBC, 0x7C, 0xB4, 0x74, 0x75, 0xB5,
    0x77, 0xB7, 0xB6, 0x76, 0x72, 0xB2, 0xB3, 0x73, 0xB1, 0x71, 0x70, 0xB0, 0x50, 0x90, 0x91,
    0x51, 0x93, 0x53, 0x52, 0x92, 0x96, 0x56, 0x57, 0x97, 0x55, 0x95, 0x94, 0x54, 0x9C, 0x5C,
    0x5D, 0x9D, 0x5F, 0x9F, 0x9E, 0x5E, 0x5A, 0x9A, 0x9B, 0x5B, 0x99, 0x59, 0x58, 0x98, 0x88,
    0x48, 0x49, 0x89, 0x4B, 0x8B, 0x8A, 0x4A, 0x4E, 0x8E, 0x8F, 0x4F, 0x8D, 0x4D, 0x4C, 0x8C,
    0x44, 0x84, 0x85, 0x45, 0x87, 0x47, 0x46, 0x86, 0x82, 0x42, 0x43, 0x83, 0x41, 0x81, 0x80,
    0x40};

        public static byte[] CRC(byte[] value)
        {
            byte uchCRCHi = 0xFF; /* 高CRC字节初始化 */
            byte uchCRCLo = 0xFF; /* 低CRC字节初始化 */
            int uIndex; /* CRC循环中的索引 */
            for (int i = 0; i < value.Length; i++)
            {
                uIndex = uchCRCLo ^ value[i];
                uchCRCLo = (byte)int.Parse((uchCRCHi ^ auchCRCHi[uIndex]).ToString("X"), System.Globalization.NumberStyles.HexNumber);
                uchCRCHi = auchCRCLo[uIndex];
            }
            byte[] crcValue = new byte[value.Length + 2];
            value.CopyTo(crcValue, 0);
            crcValue[crcValue.Length - 2] = uchCRCLo;
            crcValue[crcValue.Length - 1] = uchCRCHi;
            return crcValue;
        }

        public static byte[] RFSum(byte[] value)
        {
            byte btSum = 0;
            for (int i = 0; i < value.Length; i++)
            {
                btSum ^= value[i];
            }
            btSum ^= 0x14;
            byte[] btSumValue = new byte[value.Length + 1];
            value.CopyTo(btSumValue, 0);
            btSumValue[btSumValue.Length - 1] = btSum;
            return btSumValue;
        }
    }

    public class TimerToolkit
    {
        public AutoResetEvent CheckTime = new AutoResetEvent(false);
        public System.Threading.Timer ThreadTimer;
        public Action TimesUp;

        public int Timeout { get; set; }
        public bool IsTimeout { get; set; }
        private int currentCount;
        public int CurrentCount
        {
            get { return currentCount; }
            set
            {
                currentCount = value;
                TimeChange();
                if(CurrentCount > Timeout)
                {
                    Stop();
                    ClearCount();
                    TimesUp?.Invoke();
                }
            }
        }

        public TimerToolkit()
        {
            ThreadTimer = new System.Threading.Timer(
                new System.Threading.TimerCallback(TimerUp), null, System.Threading.Timeout.Infinite, 1000);
            CurrentCount = 0;
            IsTimeout = false;
        }

        #region 基础功能
        private void TimerUp(object value)
        {
            CurrentCount += 1;
        }
        //超时自动set，在属性变化中调用
        private void TimeChange()
        {
            if (CurrentCount > Timeout)
            {
                IsTimeout = true;
                CheckTime.Set();
            }
        }

        public void Start()
        {
            ThreadTimer.Change(0, 1000);
        }

        public void Stop()
        {
            ThreadTimer.Change(System.Threading.Timeout.Infinite, 1000);
        }

        public void ClearCount()
        {
            CurrentCount = 0;
        }
        #endregion

        #region 暂停功能
        //暂停指定的时间
        public void Suspend(int timeout)
        {
            Timeout = timeout;
            IsTimeout = false;
            Stop();
            ClearCount();
            Start();
            CheckTime.WaitOne();
            Stop();
            ClearCount();
        }
        //未超时的手动set，外部调用
        public void TimerSet()
        {
            if (CurrentCount <= Timeout)
            {
                IsTimeout = false;
                CheckTime.Set();
            }
        }
        #endregion

        #region 计时功能
        public void Time(int timeout)
        {
            Timeout = timeout;
            Stop();
            ClearCount();
            Start();
        }

        public void Reset()
        {
            Stop();
            ClearCount();
        }
        #endregion
    }
}
