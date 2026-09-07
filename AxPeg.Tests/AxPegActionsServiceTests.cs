using System.Data;
using AxPeg.Lib.Interfaces;
using AxPeg.Repositories.Interfaces;
using AxPeg.Services;
using Moq;
using Xunit;

namespace AxPeg.Tests;

public class AxPegActionsServiceTests
{
    [Fact]
    public async Task ApproveTaskAsync_commits_status_and_history_before_publishing_notification()
    {
        var repository = new Mock<IStoreDataRepository>();
        var publisher = new Mock<IRabbitMQPublisher>();
        var email = new Mock<IEmailService>();
        repository.Setup(x => x.OpenConnectionAsync("app")).ReturnsAsync(true);
        repository.Setup(x => x.ExecuteQueryAsync(It.IsAny<string>())).ReturnsAsync(CreateTaskDetails());
        repository.Setup(x => x.ExecuteNonQueryAsync(It.IsAny<string>())).ReturnsAsync(1);
        publisher.Setup(x => x.PushToQueueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var service = new AxPegActionsService(repository.Object, publisher.Object, email.Object);

        bool result = await service.ApproveTaskAsync("app", "task-1", "O'Connor", "Looks good: 'approved'.");

        Assert.True(result);
        repository.Verify(x => x.BeginTransactionAsync(), Times.Once);
        repository.Verify(x => x.CommitTransactionAsync(), Times.Once);
        repository.Verify(x => x.RollbackTransactionAsync(), Times.Never);
        repository.Verify(x => x.ExecuteNonQueryAsync(It.Is<string>(sql =>
            sql.Contains("approvedby = 'O''Connor'", StringComparison.Ordinal) &&
            sql.Contains("comments = 'Looks good: ''approved''.'", StringComparison.Ordinal))), Times.Once);
        repository.Verify(x => x.ExecuteNonQueryAsync(It.Is<string>(sql =>
            sql.Contains("INSERT INTO axactivetaskstatus", StringComparison.Ordinal))), Times.Once);
        publisher.Verify(x => x.PushToQueueAsync("app", "peg_notifications", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ApproveTaskAsync_rolls_back_without_notification_when_task_is_not_active()
    {
        var repository = new Mock<IStoreDataRepository>();
        var publisher = new Mock<IRabbitMQPublisher>();
        var email = new Mock<IEmailService>();
        repository.Setup(x => x.OpenConnectionAsync("app")).ReturnsAsync(true);
        repository.Setup(x => x.ExecuteQueryAsync(It.IsAny<string>())).ReturnsAsync(CreateTaskDetails());
        repository.Setup(x => x.ExecuteNonQueryAsync(It.IsAny<string>())).ReturnsAsync(0);
        var service = new AxPegActionsService(repository.Object, publisher.Object, email.Object);

        bool result = await service.ApproveTaskAsync("app", "task-1", "user", "comment");

        Assert.False(result);
        repository.Verify(x => x.BeginTransactionAsync(), Times.Once);
        repository.Verify(x => x.CommitTransactionAsync(), Times.Never);
        repository.Verify(x => x.RollbackTransactionAsync(), Times.Once);
        publisher.Verify(x => x.PushToQueueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ApproveTaskAsync_returns_success_when_notification_fails_after_commit()
    {
        var repository = new Mock<IStoreDataRepository>();
        var publisher = new Mock<IRabbitMQPublisher>();
        var email = new Mock<IEmailService>();
        repository.Setup(x => x.OpenConnectionAsync("app")).ReturnsAsync(true);
        repository.Setup(x => x.ExecuteQueryAsync(It.IsAny<string>())).ReturnsAsync(CreateTaskDetails());
        repository.Setup(x => x.ExecuteNonQueryAsync(It.IsAny<string>())).ReturnsAsync(1);
        publisher.Setup(x => x.PushToQueueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("RabbitMQ unavailable"));
        var service = new AxPegActionsService(repository.Object, publisher.Object, email.Object);

        bool result = await service.ApproveTaskAsync("app", "task-1", "user", "comment");

        Assert.True(result);
        repository.Verify(x => x.CommitTransactionAsync(), Times.Once);
        repository.Verify(x => x.RollbackTransactionAsync(), Times.Never);
    }

    private static DataTable CreateTaskDetails()
    {
        var table = new DataTable();
        table.Columns.Add("processname");
        table.Columns.Add("taskname");
        table.Columns.Add("tasktype");
        table.Columns.Add("indexno");
        table.Columns.Add("subindexno");
        table.Columns.Add("priorindex");
        table.Columns.Add("keyfield");
        table.Columns.Add("keyvalue");
        table.Columns.Add("transid");
        table.Rows.Add("Proc", "Review", "Approval", "1", "1", "0", "recordid", "42", "trn");
        return table;
    }
}
