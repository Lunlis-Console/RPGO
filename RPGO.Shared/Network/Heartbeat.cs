namespace RPGGame.Shared.Network;

public record PingMessage(long Seq, long ClientTime);

public record PongMessage(long Seq, long ServerTime);