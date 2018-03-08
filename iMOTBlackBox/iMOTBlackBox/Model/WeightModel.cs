using System;
using System.Collections.Generic;
using System.Text;

namespace iMOTBlackBox.Model
{
    public class WeightModel
    {
        public Data data { get; set; }
        public Patient patient { get; set; }
    }
    public class Data
    {
        public double created { get; set; }
        public double weight { get; set; }
    }
    public class Patient
    {
        public string id { get; set; }
    }
}
