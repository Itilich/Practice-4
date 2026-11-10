using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Tic_tac_toe.Data;
using Tic_tac_toe.Models;

namespace Tic_tac_toe.Hubs;

public sealed class GameHub(GameDbContext db) : Hub
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static readonly ConcurrentDictionary<string, RoomPlayers> Players = new();

    private sealed class RoomPlayers
    {
        public string? XConnectionId { get; set; }
        public string? OConnectionId { get; set; }
    }


    public async Task<string> CreateGame(int size)
    {
        if (size is < 3 or > 7) throw new HubException("Размер поля должен быть от 3 до 7.");
        var code = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var game = new SavedGame { Code = code, Size = size, BoardJson = JsonSerializer.Serialize(Enumerable.Repeat("", size * size).ToArray()) };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        await Groups.AddToGroupAsync(Context.ConnectionId, code);
        Players[code] = new RoomPlayers { XConnectionId = Context.ConnectionId };
        await Clients.Caller.SendAsync("GameCreated", ToState(game, playerMark: "X"));
        return code;
    }

    public async Task JoinGame(string code)
    {
        code = NormalizeCode(code);
        var gate = Locks.GetOrAdd(code, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var game = await db.Games.AsNoTracking().SingleOrDefaultAsync(g => g.Code == code)
                ?? throw new HubException("Игра не найдена.");
            var players = Players.GetOrAdd(code, _ => new RoomPlayers());
            if (players.OConnectionId is not null && players.OConnectionId != Context.ConnectionId)
                throw new HubException("В этой комнате уже есть соперник.");
            players.OConnectionId = Context.ConnectionId;
            await Groups.AddToGroupAsync(Context.ConnectionId, code);
            await Clients.Caller.SendAsync("GameJoined", ToState(game, playerMark: "O"));
            await Clients.Group(code).SendAsync("PlayerJoined", "Соперник подключился и играет за O.");
        }
        finally { gate.Release(); }
    }

    public async Task MakeMove(string code, int row, int column)
    {
        code = NormalizeCode(code);
        var gate = Locks.GetOrAdd(code, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var game = await db.Games.SingleOrDefaultAsync(g => g.Code == code)
                ?? throw new HubException("Игра не найдена.");
            if (!Players.TryGetValue(code, out var players)) throw new HubException("Вы не игрок этой комнаты.");
            var playerMark = players.XConnectionId == Context.ConnectionId ? "X" :
                players.OConnectionId == Context.ConnectionId ? "O" : null;
            if (playerMark is null) throw new HubException("Вы не игрок этой комнаты.");
            if (game.Status != "playing") throw new HubException("Игра уже завершена.");
            if (game.CurrentPlayer != playerMark) throw new HubException("Сейчас ход соперника.");
            if (row < 0 || row >= game.Size || column < 0 || column >= game.Size) throw new HubException("Клетка вне поля.");
            var board = JsonSerializer.Deserialize<string[]>(game.BoardJson) ?? [];
            var index = row * game.Size + column;
            if (!string.IsNullOrEmpty(board[index])) throw new HubException("Клетка уже занята.");

            var player = game.CurrentPlayer;
            board[index] = player;
            var winner = HasWon(board, game.Size, row, column, player) ? player : null;
            game.Status = winner is not null ? "won" : board.All(mark => !string.IsNullOrEmpty(mark)) ? "draw" : "playing";
            game.BoardJson = JsonSerializer.Serialize(board);
            game.CurrentPlayer = player == "X" ? "O" : "X";
            game.UpdatedUtc = DbTime.UtcPlusFiveNow;
            db.Moves.Add(new SavedMove { GameId = game.Id, Row = row, Column = column, Player = player });
            await db.SaveChangesAsync();

            await Clients.Group(code).SendAsync("StateUpdated", ToState(game, winner));
            await Clients.Caller.SendAsync("MoveAccepted", row, column, player);
        }
        finally { gate.Release(); }
    }

    public async Task ResetGame(string code)
    {
        code = NormalizeCode(code);
        var gate = Locks.GetOrAdd(code, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var game = await db.Games.SingleOrDefaultAsync(g => g.Code == code)
                ?? throw new HubException("Игра не найдена.");
            if (!Players.TryGetValue(code, out var players) ||
                (players.XConnectionId != Context.ConnectionId && players.OConnectionId != Context.ConnectionId))
                throw new HubException("Вы не игрок этой комнаты.");
            game.BoardJson = JsonSerializer.Serialize(Enumerable.Repeat("", game.Size * game.Size).ToArray());
            game.CurrentPlayer = "X";
            game.Status = "playing";
            game.UpdatedUtc = DbTime.UtcPlusFiveNow;
            await db.SaveChangesAsync();
            await Clients.Group(code).SendAsync("StateUpdated", ToState(game));
            await Clients.Caller.SendAsync("ResetAccepted", code);
        }
        finally { gate.Release(); }
    }

    private static string NormalizeCode(string code) => (code ?? "").Trim().ToUpperInvariant();
    private static GameState ToState(SavedGame game, string? winner = null, string? playerMark = null) =>
        new(game.Code, game.Size, JsonSerializer.Deserialize<string[]>(game.BoardJson) ?? [], game.CurrentPlayer, game.Status, winner, playerMark);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var entry in Players)
        {
            var gate = Locks.GetOrAdd(entry.Key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (entry.Value.XConnectionId == Context.ConnectionId) entry.Value.XConnectionId = null;
                if (entry.Value.OConnectionId == Context.ConnectionId) entry.Value.OConnectionId = null;
            }
            finally { gate.Release(); }
        }
        await base.OnDisconnectedAsync(exception);
    }

    private static bool HasWon(string[] board, int size, int row, int col, string player)
    {
        foreach (var (dr, dc) in new[] { (0, 1), (1, 0), (1, 1), (1, -1) })
        {
            var count = 1;
            foreach (var sign in new[] { -1, 1 })
            {
                var r = row + dr * sign; var c = col + dc * sign;
                while (r >= 0 && r < size && c >= 0 && c < size && board[r * size + c] == player)
                { count++; r += dr * sign; c += dc * sign; }
            }
            if (count >= size) return true;
        }
        return false;
    }
}
