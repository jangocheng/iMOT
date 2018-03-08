using System;
using System.Collections.Generic;
using System.Text;

namespace iMOTBlackBox.Model
{
    public class ContentModel
    {
        //public string CardId { get; set; }
        public int PatientId { get; set; }
        public string PatientName { get; set; }
        public double? Weight { get; set; }
        public double? DryWeight { get; set; }      
        public double? UFTarget { get; set; }
        public double? BloodFlow { get; set; }
        public double? StartValue { get; set; }
        public double? MaintainValue { get; set; }

        public List<string> CardId { get; set; } = new List<string>();
    }
}
