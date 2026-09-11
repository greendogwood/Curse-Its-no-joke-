using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ProjectDS.DbEf.Models;

[PrimaryKey("SnapshotId", "ShipId")]
public partial class SnapshotShip
{
    [Key]
    public long SnapshotId { get; set; }

    [Key]
    public Guid ShipId { get; set; }

    [StringLength(64)]
    public string Name { get; set; } = null!;

    public byte Faction { get; set; }

    public byte State { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Hp { get; set; }

    public double MaxHp { get; set; }

    [ForeignKey("SnapshotId")]
    [InverseProperty("SnapshotShips")]
    public virtual Snapshot Snapshot { get; set; } = null!;
}
