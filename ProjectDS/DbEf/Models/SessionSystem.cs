using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ProjectDS.DbEf.Models;

[PrimaryKey("SessionId", "SystemIndex")]
public partial class SessionSystem
{
    [Key]
    public long SessionId { get; set; }

    [Key]
    public int SystemIndex { get; set; }

    public int SystemId { get; set; }

    [ForeignKey("SessionId")]
    [InverseProperty("SessionSystems")]
    public virtual GameSession Session { get; set; } = null!;

    [ForeignKey("SystemId")]
    [InverseProperty("SessionSystems")]
    public virtual StarSystem System { get; set; } = null!;
}
