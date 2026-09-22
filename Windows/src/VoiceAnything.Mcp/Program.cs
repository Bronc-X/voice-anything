using System.Text;
using SayAll.Core.History;

if (args.Length != 0 && (args.Length != 2 || args[0] != "--data-dir"))
{
    Console.Error.WriteLine("Usage: VoiceAnything.Mcp [--data-dir <directory>]");
    return 2;
}
Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
var server = new HistoryMcpServer(new JournalStore(args.Length == 2 ? args[1] : JournalStore.DefaultDirectory),
    Environment.GetEnvironmentVariable("VOICE_ANYTHING_MCP_TOKEN"));
var buffer = new char[1];
var line = new StringBuilder();
var oversized = false;
while (await Console.In.ReadAsync(buffer.AsMemory()) > 0)
{
    if (buffer[0] != '\n')
    {
        if (line.Length < 65_537) line.Append(buffer[0]);
        else oversized = true;
        continue;
    }
    var response = server.Handle(oversized ? new string(' ', 65_537) : line.ToString().TrimEnd('\r'));
    if (response is not null) { await Console.Out.WriteLineAsync(response); await Console.Out.FlushAsync(); }
    line.Clear();
    oversized = false;
}
return 0;
