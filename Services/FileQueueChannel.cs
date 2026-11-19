using System.Threading.Channels;

namespace FileVault.Services;

public sealed class FileQueueChannel
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions
        {
           SingleReader = true,
           SingleWriter = false 
        });

    public ChannelWriter<string> Writer => _channel.Writer;
    public ChannelReader<string> Reader => _channel.Reader;
}