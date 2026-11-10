namespace Tic_tac_toe.Models;

public sealed record GameState(string Code, int Size, string[] Board, string CurrentPlayer, string Status, string? Winner, string? PlayerMark);
