using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace iMOTBlackBox.EF.Model
{
    public class Patient
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; }

        [InverseProperty("Patients")]
        public virtual ICollection<LongOrder> LongOrder { get; set; } = new HashSet<LongOrder>();

        [InverseProperty("Patients")]
        public virtual ICollection<PatientsCard> PatientsCard { get; set; } = new HashSet<PatientsCard>();
    }
}
