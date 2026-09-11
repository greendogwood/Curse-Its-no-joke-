using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ProjectDS.DbEf.Models;

public partial class StarSystem
{
    [Key]
    public int SystemId { get; set; }

    [StringLength(64)]
    public string Name { get; set; } = null!;

    [StringLength(64)]
    public string PlanetName { get; set; } = null!;

    public double PlanetX { get; set; }

    public double PlanetY { get; set; }

    [InverseProperty("System")]
    public virtual ICollection<SessionSystem> SessionSystems { get; set; } = new List<SessionSystem>();
}
