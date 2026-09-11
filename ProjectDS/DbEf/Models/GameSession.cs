using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ProjectDS.DbEf.Models;

public partial class GameSession
{
    [Key]
    public long SessionId { get; set; }

    public int PlayerId { get; set; }

    public DateTime StartedUtc { get; set; }

    public DateTime? EndedUtc { get; set; }

    public int? FinalScore { get; set; }

    public int? FinalTurn { get; set; }

    public bool IsGameOver { get; set; }

    [ForeignKey("PlayerId")]
    [InverseProperty("GameSessions")]
    public virtual Player Player { get; set; } = null!;

    [InverseProperty("Session")]
    public virtual ICollection<SessionEvent> SessionEvents { get; set; } = new List<SessionEvent>();

    [InverseProperty("Session")]
    public virtual ICollection<SessionSystem> SessionSystems { get; set; } = new List<SessionSystem>();

    [InverseProperty("Session")]
    public virtual ICollection<Snapshot> Snapshots { get; set; } = new List<Snapshot>();
}
