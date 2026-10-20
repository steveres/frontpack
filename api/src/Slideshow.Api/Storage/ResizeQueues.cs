using System.Text.Json;
using System.Threading.Channels;
using Azure.Core;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;

namespace Slideshow.Api.Storage;

/// <summary>A photo whose smaller versions are to be made.</summary>
public sealed record ResizeJob(string OwnerId, Guid AlbumId, Guid MediaId);

/// <param name="Attempt">1 the first time the job is handed out, 2 after a retry, and so on.</param>
/// <param name="Receipt">What the queue needs to finish or retry this delivery.</param>
public sealed record QueuedJob(ResizeJob Job, int Attempt, object Receipt);

/// <summary>
/// Work for the background resizer: Azure Storage Queues in the cloud (so nothing is lost when the API
/// stops or scales to zero), memory locally and in tests.
/// </summary>
public interface IResizeQueue
{
    Task EnqueueAsync(ResizeJob job, CancellationToken ct);
    /// <summary>The next job; waits until there is one (memory), or returns null after a while without (Azure).</summary>
    Task<QueuedJob?> ReceiveAsync(CancellationToken ct);
    /// <summary>Done with it: it won't be handed out again.</summary>
    Task CompleteAsync(QueuedJob job, CancellationToken ct);
    /// <summary>Failed this time: hand it out again after `delay`.</summary>
    Task RetryLaterAsync(QueuedJob job, TimeSpan delay, CancellationToken ct);
}

/// <summary>Local development and tests. Jobs are lost when the API stops: photos still waiting then stay full size.</summary>
public sealed class MemoryResizeQueue : IResizeQueue
{
    private readonly Channel<(ResizeJob Job, int Attempt)> _jobs = Channel.CreateUnbounded<(ResizeJob, int)>();

    public Task EnqueueAsync(ResizeJob job, CancellationToken ct) => _jobs.Writer.WriteAsync((job, 1), ct).AsTask();

    public async Task<QueuedJob?> ReceiveAsync(CancellationToken ct)
    {
        var (job, attempt) = await _jobs.Reader.ReadAsync(ct);
        return new QueuedJob(job, attempt, attempt);
    }

    public Task CompleteAsync(QueuedJob job, CancellationToken ct) => Task.CompletedTask;

    public Task RetryLaterAsync(QueuedJob job, TimeSpan delay, CancellationToken ct)
    {
        _ = Task.Delay(delay, CancellationToken.None).ContinueWith(_ => _jobs.Writer.TryWrite((job.Job, job.Attempt + 1)), TaskScheduler.Default);
        return Task.CompletedTask;
    }
}

/// <summary>
/// An Azure Storage queue. A received job is hidden from other receivers while it's worked on; if the
/// API stops before finishing it (scaled in, restarted), it reappears and is done again.
/// </summary>
public sealed class AzureResizeQueue : IResizeQueue
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan WorkTime = TimeSpan.FromMinutes(5); // hidden this long once received
    private static readonly TimeSpan MaxIdleWait = TimeSpan.FromSeconds(30);
    private readonly QueueClient _queue;
    private readonly SemaphoreSlim _wake = new(0, 1); // released by an upload here, so our own worker doesn't sit out its wait
    private TimeSpan _idleWait = TimeSpan.FromSeconds(1);

    private sealed record Receipt(string MessageId, string PopReceipt);

    /// <param name="credential">Signs in to Azure (not used with a connection string, i.e. Azurite).</param>
    public AzureResizeQueue(IOptions<ResizeOptions> options, TokenCredential credential)
    {
        var o = options.Value;
        var clientOptions = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
        var service = !string.IsNullOrEmpty(o.ConnectionString)
            ? new QueueServiceClient(o.ConnectionString, clientOptions)
            : new QueueServiceClient(new Uri(o.QueueServiceUri ?? throw new InvalidOperationException("Resize:QueueServiceUri is not set.")), credential, clientOptions);
        _queue = service.GetQueueClient(o.QueueName);
    }

    /// <summary>Creates the queue if missing (the local emulator; in Azure, main.bicep creates it).</summary>
    public Task EnsureQueueAsync(CancellationToken ct) => _queue.CreateIfNotExistsAsync(cancellationToken: ct);

    public async Task EnqueueAsync(ResizeJob job, CancellationToken ct)
    {
        await _queue.SendMessageAsync(new BinaryData(JsonSerializer.SerializeToUtf8Bytes(job, Json)), cancellationToken: ct);
        try { _wake.Release(); } catch (SemaphoreFullException) { } // already woken
    }

    public async Task<QueuedJob?> ReceiveAsync(CancellationToken ct)
    {
        var message = (await _queue.ReceiveMessageAsync(WorkTime, ct)).Value;
        if (message is null)
        {
            // Nothing to do: ask less often the longer it stays quiet (each ask is a billed storage call),
            // unless a photo is uploaded here meanwhile.
            var woken = await _wake.WaitAsync(_idleWait, ct);
            _idleWait = woken ? TimeSpan.FromSeconds(1) : TimeSpan.FromTicks(Math.Min(MaxIdleWait.Ticks, _idleWait.Ticks * 2));
            return null;
        }
        _idleWait = TimeSpan.FromSeconds(1);
        ResizeJob? job;
        try { job = message.Body.ToObjectFromJson<ResizeJob>(Json); }
        catch (JsonException) { job = null; }
        if (job?.OwnerId is null)
        {
            await _queue.DeleteMessageAsync(message.MessageId, message.PopReceipt, ct); // not one of ours: drop it
            return null;
        }
        return new QueuedJob(job, (int)message.DequeueCount, new Receipt(message.MessageId, message.PopReceipt));
    }

    public async Task CompleteAsync(QueuedJob job, CancellationToken ct)
    {
        var r = (Receipt)job.Receipt;
        await _queue.DeleteMessageAsync(r.MessageId, r.PopReceipt, ct);
    }

    public async Task RetryLaterAsync(QueuedJob job, TimeSpan delay, CancellationToken ct)
    {
        var r = (Receipt)job.Receipt;
        await _queue.UpdateMessageAsync(r.MessageId, r.PopReceipt, visibilityTimeout: delay, cancellationToken: ct);
    }
}
