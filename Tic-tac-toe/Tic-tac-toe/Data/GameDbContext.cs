using Microsoft.EntityFrameworkCore;

namespace Tic_tac_toe.Data;

public sealed class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<SavedGame> Games => Set<SavedGame>();
    public DbSet<SavedMove> Moves => Set<SavedMove>();
}

public sealed class SavedGame
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int Size { get; set; }
    public string BoardJson { get; set; } = "[]";
    public string CurrentPlayer { get; set; } = "X";
    public string Status { get; set; } = "playing";
    public DateTime CreatedUtc { get; set; } = DbTime.UtcPlusFiveNow;
    public DateTime UpdatedUtc { get; set; } = DbTime.UtcPlusFiveNow;
}

public sealed class SavedMove
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int Row { get; set; }
    public int Column { get; set; }
    public string Player { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DbTime.UtcPlusFiveNow;
}

public static class DbTime
{
    public static DateTime UtcPlusFiveNow =>
        DateTime.SpecifyKind(DateTime.UtcNow.AddHours(5), DateTimeKind.Unspecified);
}
