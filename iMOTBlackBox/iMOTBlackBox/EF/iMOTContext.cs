using iMOTBlackBox.EF.Model;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace iMOTBlackBox.EF
{
    public class iMOTContext : DbContext
    {
        private string _connectionString;

        public iMOTContext(string connectionString)
        {
            _connectionString = connectionString;
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(_connectionString);
        }

        public DbSet<Patient> Patients { get; set; }
        public DbSet<PatientsCard> PatientsCards { get; set; }
        public DbSet<LongOrder> LongOrder { get; set; }
    }
}
