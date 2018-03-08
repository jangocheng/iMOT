using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace iMOTBlackBox.EF.Model
{
    public class LongOrder
    {
        [Key]
        public int Id { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime? Date { get; set; }
        public double? DryWeight { get; set; }
        public double? BloodFlow { get; set; }
        public double? StartValue { get; set; }
        public double? MaintainValue { get; set; }
        public int PatientId { get; set; }

        [ForeignKey("PatientId")]
        public virtual Patient Patients { get; set; } = new Patient();
    }
}
