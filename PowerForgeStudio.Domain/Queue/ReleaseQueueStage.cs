namespace PowerForgeStudio.Domain.Queue;

public enum ReleaseQueueStage
{
    Prepare = 0,
    Build = 1,
    Sign = 2,
    Publish = 3,
    Verify = 4,
    Completed = 5
}

