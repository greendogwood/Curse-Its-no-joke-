using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ProjectDS.DbEf.Models;

public partial class Player
{
    [Key]
    public int PlayerId { get; set; }

    [StringLength(64)]
    public string Name { get; set; } = null!;

    public DateTime CreatedUtc { get; set; }

    [InverseProperty("Player")]
    public virtual ICollection<GameSession> GameSessions { get; set; } = new List<GameSession>();
}
