using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using System.Xml.Linq;

namespace ConsoleTest.TestCode
{
    public static class Test
    {
        #region Json
        readonly static Dictionary<string, string> pairs = new() { { "123", "321" }, { "221", "321" }, { "222", "321" } };
        public static void JsonTest()
        {
            string JsonString = JsonSerializer.Serialize(pairs);
            Console.WriteLine(JsonString);
            Dictionary<string, string>? result = JsonSerializer.Deserialize<Dictionary<string, string>>(JsonString);
        }
        #endregion

        #region Path
        public static void PathTest()
        {
            Console.WriteLine(Environment.GetEnvironmentVariable("IpfsHttpApi") ?? "http://localhost:5001");
            string path1 = Path.Combine("ddd", "d.c");
            string path2 = ("file\\fff").Split('\\').LastOrDefault("nofile");
            ushort length = BitConverter.ToUInt16(new byte[] { 0xdd, 0x00 }, 0);
            Console.WriteLine(AppDomain.CurrentDomain.BaseDirectory);
        }
        #endregion

        #region 排序
        static string[] PermArray = new string[] { "1", "2", "3" };
        public static void Perm(string[] a, int k, int n)
        {
            int i;
            if (k == n)
            {
                foreach (var item in a)
                {
                    Console.Write(item + " ");
                }
                Console.WriteLine();
            }
            else
            {
                string t;
                for (i = k; i < n; i++)
                {
                    t = a[k]; a[k] = a[i]; a[i] = t;
                    Perm(a, k + 1, n);
                    t = a[k]; a[k] = a[i]; a[i] = t;
                }
            }
        }
        //perm(aa, 0, aa.Length); 
        #endregion

        #region 判断语句
        static int djn = PermArray.Length == 3 ? -1 : PermArray.GetHashCode();
        //Console.WriteLine(djn);
        #endregion

        #region Property
        public static void PropertyTest()
        {
            Part part = new Part("123", 1) { PartName = "321" };
            var properties = part.GetType().GetProperties();
            foreach (PropertyInfo property in properties)
            {
                Console.Write(property.Name + ":");
                Console.WriteLine(property.GetValue(part));
                Console.WriteLine(property.GetValue(part)!.GetType());
            }
        }

        public static void PropertySet()
        {
            Part part = new("123", 1) { PartName = "321" };
            PropertyInfo[] properties = part.GetType().GetProperties();
            foreach (PropertyInfo property in properties)
            {
                property.SetValue(part, Convert.ChangeType("1", property.PropertyType));
                Console.Write(property.Name + ":");
                Console.WriteLine(property.GetValue(part));
                Console.WriteLine(property.PropertyType);
                Console.WriteLine(property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null);
            }
        }

        public static void SetPropertyValue(object obj, string name, string value)
        {
            PropertyInfo? p = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p != null)
            {
                object? dymicValue;
                if (p.PropertyType.IsArray)
                {
                    p.SetValue(obj, value, null);
                }
                else
                {
                    if (string.IsNullOrEmpty(value))
                    {
                        dymicValue = p.PropertyType.IsValueType ? Activator.CreateInstance(p.PropertyType) : null;
                    }
                    else
                    {
                        dymicValue = System.ComponentModel.TypeDescriptor.GetConverter(p.PropertyType).ConvertFromString(value);
                    }
                    p.SetValue(obj, dymicValue, null);
                }
            }
        }
        #endregion

        #region ResetEvent
        static ManualResetEvent resetEvent = new(true);
        public static void ResetEventTest()
        {
            while (true)
            {
                Thread.Sleep(1000);
                resetEvent.WaitOne();
                Console.WriteLine("123");
            }
        }
        #endregion

        #region String
        public static void StringTest()
        {
            string s1 = $"1111111111111111111{Environment.NewLine}22222222222222222222222222222{Environment.NewLine}3333333333333";
            //Console.WriteLine(s1);
            string s2 = s1.Substring(s1.Length / 2);
            //Console.WriteLine(s2);
        }
        #endregion

        #region Channel
        static readonly Channel<string> TestChannel = Channel.CreateUnbounded<string>();

        static async Task Write()
        {
            int count = 0;
            while (true)
            {
                resetEvent.WaitOne();
                await Task.Delay(1000);
                count++;
                await TestChannel.Writer.WriteAsync($"写入{count}");
            }
        }

        static async Task ReadAsync()
        {
            while (await TestChannel.Reader.WaitToReadAsync())
            {
                Console.WriteLine("读取到的数据：");
                if (TestChannel.Reader.TryRead(out string? message))
                {
                    Console.WriteLine(message);
                }
                //var result = await TestChannel.Reader.ReadAsync();
                //Console.WriteLine(result);
            }
        }

        #endregion

        #region 转换
        public static void ConvertVariable<T>(string variable)
        {
            T value = (T)Convert.ChangeType(variable, typeof(T));
            Console.WriteLine(value.GetType().ToString());
            Console.WriteLine(value);
        }

        //ConvertVariable<double>("3.14");
        //ConvertVariable<int>("3");
        //ConvertVariable<bool>("true");
        //ConvertVariable<bool>("false");
        //ConvertVariable<double>("0");
        #endregion

        #region Task
        public static void RunTest(object? state)
        {
            Console.WriteLine(state);
        }
        //Task.Factory.StartNew(RunTest, TestChannel);
        #endregion



    }
}
