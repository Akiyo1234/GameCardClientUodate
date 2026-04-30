using Postgrest.Attributes;
using Postgrest.Models;
using System;

[Table("matchmaking_queue")]
public class MatchmakingQueueData : BaseModel
{
    [PrimaryKey("id", false)]
    public int Id { get; set; }

    [Column("player_id")]
    public string PlayerId { get; set; }

    [Column("status")]
    public string Status { get; set; }

    [Column("room_code")]
    public string RoomCode { get; set; }

    [Column("matched_player_id")]
    public string MatchedPlayerId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("matched_at")]
    public DateTime? MatchedAt { get; set; }
}
