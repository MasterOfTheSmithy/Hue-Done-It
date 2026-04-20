// File: Assets/_Project/Tasks/IRepairTaskFloodSafetyProvider.cs
namespace HueDoneIt.Tasks
{
    public interface IRepairTaskFloodSafetyProvider
    {
        bool IsTaskEnvironmentSafe(NetworkRepairTask task);
    }
}
