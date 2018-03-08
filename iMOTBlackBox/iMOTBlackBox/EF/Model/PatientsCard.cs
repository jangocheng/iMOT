using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace iMOTBlackBox.EF.Model
{
    public class PatientsCard
    {
        [Key]
        public int Id { get; set; }
        public string CardNumber { get; set; }
        public int PatientId { get; set; }

        [ForeignKey("PatientId")]
        public virtual Patient Patients { get; set; } = new Patient();
    }
}
