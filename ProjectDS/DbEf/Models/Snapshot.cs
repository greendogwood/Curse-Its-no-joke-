using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ProjectDS.DbEf.Models;

[Index("SessionId", "Turn", Name = "IX_Snapshots_Session_Turn")]
public partial class Snapshot
{
    [Key]
    public long SnapshotId { get; set; }

    public long SessionId { get; set; }

    public int Turn { get; set; }

    public int CurrentSystemIndex { get; set; }

    public int Score { get; set; }

    public Guid PlayerIdGuid { get; set; }

    public double PlayerHp { get; set; }

    public double PlayerMaxHp { get; set; }

    public byte PlayerState { get; set; }

    public int PlayerCooldown { get; set; }

    public double PlayerX { get; set; }

    public double PlayerY { get; set; }

    public Guid? SelectedTargetId { get; set; }

    public DateTime CreatedUtc { get; set; }

    [ForeignKey("SessionId")]
    [InverseProperty("Snapshots")]
    public virtual GameSession Session { get; set; } = null!;

    [InverseProperty("Snapshot")]
    public virtual ICollection<SnapshotShip> SnapshotShips { get; set; } = new List<SnapshotShip>();
}
