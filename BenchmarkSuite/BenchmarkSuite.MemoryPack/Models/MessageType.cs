namespace BenchmarkSuite.MemoryPack.Models;

public enum MessageType
{
    Unknown = 0,
    ServerUpdate = 1,
    PlayerUpdate = 2,
    EntityUpdate = 3,


    GemUpdate = 6,

    PowerUpUpdate = 7,

    ProjectileUpdate = 8,

    MonsterUpdate = 9,

    BatchMonsterUpdate = 10,

    GameStart = 11,

    LevelStart = 12,

    LevelSyncRequest = 13,

    LevelSyncResponse = 14,

    BoulderUpdate = 15,

    MenuEntryUpdate = 16,

    PlayerTeamUpdate = 17,

    StageUpdate = 18,

    PulseUpdate = 19,

    LevelSyncNotification = 20,

    LevelSyncCompleteNotification = 21,

    TileBreakUpdate = 22,

    GameplayUpdate = 23,

    LevelCompletedNotification = 24,
}