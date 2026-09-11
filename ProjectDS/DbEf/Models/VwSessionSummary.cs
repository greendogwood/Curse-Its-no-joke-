using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ProjectDS.DbEf.Models;

[Keyless]
public partial class VwSessionSummary
{
    public long SessionId { get; set; }

    [StringLength(64)]
    public string PlayerName { get; set; } = null!;

    public DateTime StartedUtc { get; set; }

    public DateTime? EndedUtc { get; set; }

    public bool IsGameOver { get; set; }

    public int? LastTurn { get; set; }

    public int? LastScore { get; set; }

    public int? SnapshotsCount { get; set; }
}
