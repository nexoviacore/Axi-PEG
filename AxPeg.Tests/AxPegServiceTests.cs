using System.Data;
using AxPeg.Lib.Interfaces;
using AxPeg.Repositories.Interfaces;
using AxPeg.Services;
using Moq;
using Xunit;

namespace AxPeg.Tests;

public class AxPegServiceTests
{
    [Fact]
    public async Task CanInitiatePEGAsync_commits_same_index_check_as_one_transaction()
    {
        var repository = new Mock<IStoreDataRepository>();
        var cache = new Mock<IRedisCacheService>();
        repository.Setup(x => x.OpenConnectionAsync("app")).ReturnsAsync(true);
        repository.Setup(x => x.ExecuteQueryAsync(It.IsAny<string>())).ReturnsAsync(new DataTable());
        var service = new AxPegService(repository.Object, cache.Object);

        bool result = await service.CanInitiatePEGAsync("app", "Process", "Task", "1", "key");

        Assert.True(result);
        repository.Verify(x => x.BeginTransactionAsync(), Times.Once);
        repository.Verify(x => x.CommitTransactionAsync(), Times.Once);
        repository.Verify(x => x.RollbackTransactionAsync(), Times.Never);
    }

    [Fact]
    public async Task CanInitiatePEGAsync_rolls_back_when_task_creation_fails()
    {
        var missingTasks = new DataTable();
        missingTasks.Columns.Add("taskname");
        missingTasks.Columns.Add("tasktype");
        missingTasks.Columns.Add("indexno");
        missingTasks.Rows.Add("Peer", "Approval", "1");

        var repository = new Mock<IStoreDataRepository>();
        var cache = new Mock<IRedisCacheService>();
        repository.Setup(x => x.OpenConnectionAsync("app")).ReturnsAsync(true);
        repository.SetupSequence(x => x.ExecuteQueryAsync(It.IsAny<string>())).ReturnsAsync(missingTasks);
        repository.Setup(x => x.ExecuteNonQueryAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("database failure"));
        var service = new AxPegService(repository.Object, cache.Object);

        bool result = await service.CanInitiatePEGAsync("app", "Process", "Task", "1", "key");

        Assert.False(result);
        repository.Verify(x => x.BeginTransactionAsync(), Times.Once);
        repository.Verify(x => x.CommitTransactionAsync(), Times.Never);
        repository.Verify(x => x.RollbackTransactionAsync(), Times.Once);
    }
}
