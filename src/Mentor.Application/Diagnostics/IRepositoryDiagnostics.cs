namespace Mentor.Application.Diagnostics;

public interface IRepositoryDiagnostics
{
    void Probe();
    QueueStatistics GetQueueStatistics();
}
