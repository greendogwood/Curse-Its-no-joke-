using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ProjectDS.DbEf.Models;

[Index("SessionId", "Turn", Name = "IX_Events_Session_Turn")]
public partial class SessionEvent
{
    [Key]
    public long EventId { get; set; }

    public long SessionId { get; set; }

    public int Turn { get; set; }

    [StringLength(32)]
    public string Type { get; set; } = null!;

    [StringLength(400)]
    public string Message { get; set; } = null!;

    public DateTime CreatedUtc { get; set; }

    [ForeignKey("SessionId")]
    [InverseProperty("SessionEvents")]
    public virtual GameSession Session { get; set; } = null!;
}
