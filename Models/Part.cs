using System;
using System.Collections.Generic;
using System.Text;

namespace ConsoleTest
{
    public class Part
    {
        public string? PartID { get; set; }
        public string? PartName { get; set; }
        public int AmountNeeded { get; set; }

        public Part(string partID, int amountNeeded)
        {
            PartID = partID;
            AmountNeeded = amountNeeded;
        }

        public Part()
        {

        }

        public string ShowData()
        {
            return string.Format("零件ID：{0}  零件代号：{1}  目标数量：{2}\n", PartID, PartName, AmountNeeded);
        }
    }
}
