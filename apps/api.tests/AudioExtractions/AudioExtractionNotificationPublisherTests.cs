using System.Reflection;
using Api.AudioExtractions.Contracts;
using Api.AudioExtractions.Notifications;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace AudioApi.Tests.AudioExtractions;

public sealed class AudioExtractionNotificationPublisherTests
{
    [Fact]
    public async Task Publisher_sends_only_to_the_hashed_request_group()
    {
        const string requestId = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var context = DispatchProxy.Create<IHubContext<AudioExtractionsHub>, RecordingHubContext>();
        var recorder = (RecordingHubContext)(object)context;
        var publisher = new AudioExtractionNotificationPublisher(context);
        var notification = new AudioExtractionStatusResponse(
            requestId,
            "completed",
            "A extração de áudio solicitada foi concluída.",
            DateTimeOffset.UtcNow,
            new AudioExtractionResultResponse(
                $"/api/audio-extractions/{requestId}/audio",
                30,
                "audio/mpeg"));

        await publisher.PublishAsync(notification, CancellationToken.None);

        Assert.Equal(AudioExtractionsHub.GetGroupName(requestId), recorder.ClientsProxy.GroupName);
        Assert.DoesNotContain(requestId, recorder.ClientsProxy.GroupName);
        Assert.Equal("extractionUpdated", recorder.ClientsProxy.ClientProxy.MethodName);
        Assert.Same(notification, recorder.ClientsProxy.ClientProxy.Arguments![0]);
    }

    public class RecordingHubContext : DispatchProxy
    {
        public RecordingHubClients ClientsProxy { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "get_Clients" => ClientsProxy.Proxy,
                "get_Groups" => DispatchProxy.Create<IGroupManager, EmptyGroupManager>(),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
        }
    }

    public class RecordingHubClients
    {
        public string? GroupName { get; private set; }
        public RecordingClientProxy ClientProxy { get; } = new();
        public IHubClients Proxy { get; }

        public RecordingHubClients()
        {
            Proxy = DispatchProxy.Create<IHubClients, RecordingHubClientsProxy>();
            ((RecordingHubClientsProxy)(object)Proxy).Owner = this;
        }

        public IClientProxy Group(string name)
        {
            GroupName = name;
            return ClientProxy.Proxy;
        }
    }

    public class RecordingHubClientsProxy : DispatchProxy
    {
        public RecordingHubClients Owner { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == "Group"
                ? Owner.Group((string)args![0]!)
                : throw new NotSupportedException(targetMethod?.Name);
    }

    public class RecordingClientProxy
    {
        public string? MethodName { get; private set; }
        public object?[]? Arguments { get; private set; }
        public IClientProxy Proxy { get; } =
            DispatchProxy.Create<IClientProxy, RecordingClientProxyProxy>();

        public RecordingClientProxy()
        {
            ((RecordingClientProxyProxy)(object)Proxy).Owner = this;
        }

        public Task SendCoreAsync(string method, object?[] arguments, CancellationToken cancellationToken)
        {
            MethodName = method;
            Arguments = arguments;
            return Task.CompletedTask;
        }
    }

    public class RecordingClientProxyProxy : DispatchProxy
    {
        public RecordingClientProxy Owner { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == "SendCoreAsync"
                ? Owner.SendCoreAsync(
                    (string)args![0]!,
                    (object?[])args[1]!,
                    (CancellationToken)args[2]!)
                : throw new NotSupportedException(targetMethod?.Name);
    }

    public class EmptyGroupManager : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException(targetMethod?.Name);
    }
}
