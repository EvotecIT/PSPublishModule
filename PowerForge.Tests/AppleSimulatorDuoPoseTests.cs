using System.IO.Compression;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class AppleSimulatorDuoPoseTests
{
    private const string Device = "11111111-1111-1111-1111-111111111111";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Probe_reads_size_and_tells_a_live_display_from_a_black_one_for_every_png_filter(byte filter)
    {
        var live = AppleDuoScreenshotProbe.Read(new MemoryStream(Png(64, 48, lit: true, filter)));
        var black = AppleDuoScreenshotProbe.Read(new MemoryStream(Png(64, 48, lit: false, filter)));

        Assert.Equal((64, 48, true), (live.Width, live.Height, live.Live));
        Assert.Equal((64, 48, false), (black.Width, black.Height, black.Live));
    }

    [Fact]
    public async Task Pose_that_is_already_live_runs_the_command_with_its_display_and_size()
    {
        using var fixture = new Fixture { LiveDisplay = "primary" };
        IReadOnlyDictionary<string, string?>? environment = null;
        fixture.Validation = request => environment = request.EnvironmentVariables;

        var result = await fixture.Service.RunAsync(fixture.Request(AppleDuoPose.Closed, foldTool: true));

        Assert.True(result.Success, result.Error);
        Assert.Equal("already", result.DuoPose!.Method);
        Assert.Empty(fixture.FoldToolCalls);
        Assert.Equal("closed", environment!["POWERFORGE_DUO_POSE"]);
        Assert.Equal("primary", environment["POWERFORGE_DUO_DISPLAY"]);
        Assert.Equal("1398x2034", environment["POWERFORGE_DUO_SIZE"]);
        Assert.Equal("kept", environment["CALLER"]);
    }

    [Fact]
    public async Task Fold_tool_sets_the_pose_even_when_it_reports_failure_and_the_screenshot_confirms_it()
    {
        using var fixture = new Fixture { LiveDisplay = "primary", FoldExitCode = 1 };

        var result = await fixture.Service.RunAsync(fixture.Request(AppleDuoPose.Open, foldTool: true));

        Assert.True(result.Success, result.Error);
        Assert.Equal("fold-tool", result.DuoPose!.Method);
        Assert.Equal((2853, 2007, "landscape"), (result.DuoPose.Width, result.DuoPose.Height, result.DuoPose.Orientation));
        Assert.Equal(new[] { "--version", $"-d {Device} open --duration 1" }, fixture.FoldToolCalls);
        Assert.Empty(fixture.Prompt.ToString());
    }

    [Fact]
    public async Task Fold_tool_with_another_version_is_not_run_and_the_person_is_asked_instead()
    {
        using var fixture = new Fixture { LiveDisplay = "primary", FoldToolVersion = "duoctl 0.3.0", PersonOpensAfterProbes = 3 };

        var result = await fixture.Service.RunAsync(fixture.Request(AppleDuoPose.Open, foldTool: true));

        Assert.True(result.Success, result.Error);
        Assert.Equal("prompt", result.DuoPose!.Method);
        Assert.Contains("0.3.0", result.DuoPose.FoldToolNote);
        Assert.Equal(new[] { "--version" }, fixture.FoldToolCalls);
        Assert.Contains("in Device Hub, choose Open", fixture.Prompt.ToString());
    }

    [Fact]
    public async Task Wrong_orientation_after_one_rotate_falls_back_to_the_prompt_and_times_out_without_running_the_command()
    {
        using var fixture = new Fixture { LiveDisplay = "primary" };
        var invoked = false;
        fixture.Validation = _ => invoked = true;
        var request = fixture.Request(AppleDuoPose.Open, foldTool: true, AppleDuoOrientation.Portrait);

        var result = await fixture.Service.RunAsync(request);

        Assert.False(result.Success);
        Assert.False(invoked);
        Assert.Contains("wasn't live in portrait", result.Error);
        Assert.Equal(new[] { "--version", $"-d {Device} open --duration 1", $"-d {Device} rotate portrait" }, fixture.FoldToolCalls);
        Assert.True(result.Receipt.CleanupSucceeded);
        Assert.Equal("Shutdown", fixture.State);
    }

    internal static byte[] Png(int width, int height, bool lit, byte filter)
    {
        const int channels = 4;
        var stride = width * channels;
        var previous = new byte[stride];
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            var row = new byte[stride];
            for (var x = 0; x < width; x++)
            {
                var shade = lit && (x + y) % 3 == 0 ? (byte)(120 + (x * 7 + y * 3) % 100) : (byte)0;
                row[x * channels] = shade; row[x * channels + 1] = shade; row[x * channels + 2] = shade; row[x * channels + 3] = 255;
            }
            raw.WriteByte(filter);
            for (var i = 0; i < stride; i++)
            {
                var left = i >= channels ? row[i - channels] : 0;
                var upLeft = i >= channels ? previous[i - channels] : 0;
                var predicted = filter switch
                {
                    1 => left,
                    2 => previous[i],
                    3 => (left + previous[i]) / 2,
                    4 => Paeth(left, previous[i], upLeft),
                    _ => 0
                };
                raw.WriteByte((byte)(row[i] - predicted));
            }
            previous = row;
        }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true)) zlib.Write(raw.ToArray());
        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 });
        var header = new byte[13];
        BigEndian(header, 0, width); BigEndian(header, 4, height);
        header[8] = 8; header[9] = 6;
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BigEndian(length, 0, data.Length);
        stream.Write(length);
        stream.Write(System.Text.Encoding.ASCII.GetBytes(type));
        stream.Write(data);
        stream.Write(new byte[4]); // The probe doesn't verify CRCs.
    }

    private static void BigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24); buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8); buffer[offset + 3] = (byte)value;
    }

    private static ProcessRunResult Result(int exitCode = 0, string output = "")
        => new(exitCode, output, "", "fixture", TimeSpan.Zero, timedOut: false);

    private sealed class DelegateRunner(Func<ProcessRunRequest, ProcessRunResult> run) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(run(request));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "Duo-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, byte[]> _images = new();
        private int _probes;
        internal string State { get; private set; } = "Shutdown";
        internal string? LiveDisplay { get; set; }
        internal int FoldExitCode { get; init; }
        internal string FoldToolVersion { get; init; } = "duoctl " + AppleSimulatorSessionService.PinnedDuoFoldToolVersion;
        internal int PersonOpensAfterProbes { get; init; } = int.MaxValue;
        internal List<string> FoldToolCalls { get; } = new();
        internal StringWriter Prompt { get; } = new();
        internal Action<ProcessRunRequest> Validation { get; set; } = _ => { };
        internal AppleSimulatorSessionService Service { get; }
        private string FoldTool => Path.Combine(_root, "duoctl");

        internal Fixture()
        {
            Directory.CreateDirectory(Path.Combine(_root, "devices"));
            File.WriteAllText(FoldTool, "fixture");
            Service = new AppleSimulatorSessionService(new DelegateRunner(Simulator),
                new DelegateRunner(request => { Validation(request); return Result(); }), Prompt, (_, _) => Task.CompletedTask);
        }

        internal AppleSimulatorSessionRequest Request(AppleDuoPose pose, bool foldTool, AppleDuoOrientation orientation = AppleDuoOrientation.Any) => new()
        {
            DeviceId = Device, Owner = "duo-test", StateRoot = Path.Combine(_root, "state"), DeviceSetPath = Path.Combine(_root, "devices"),
            Command = new ProcessRunRequest("capture", _root, Array.Empty<string>(), TimeSpan.FromSeconds(10),
                new Dictionary<string, string?> { ["CALLER"] = "kept" }),
            DuoPose = new AppleDuoPoseRequest
            {
                Pose = pose, Orientation = orientation, FoldToolPath = foldTool ? FoldTool : null,
                PromptTimeout = TimeSpan.FromSeconds(5), FoldTimeout = TimeSpan.FromSeconds(5)
            }
        };

        private ProcessRunResult Simulator(ProcessRunRequest request)
        {
            if (request.FileName == FoldTool) return FoldToolCommand(request.Arguments);
            var command = request.Arguments[3];
            switch (command)
            {
                case "list":
                    var devices = new[] { new { udid = Device, state = State, isAvailable = true } };
                    return Result(output: JsonSerializer.Serialize(new { devices = new Dictionary<string, object> { ["iOS-fixture"] = devices } }));
                case "boot": State = "Booted"; return Result();
                case "bootstatus": return Result();
                case "shutdown": State = "Shutdown"; return Result();
                case "io":
                    if (++_probes > PersonOpensAfterProbes) LiveDisplay = "primary-1";
                    var display = request.Arguments[6].Substring("--display=".Length);
                    var (width, height) = display == "primary" ? (1398, 2034) : (2853, 2007);
                    File.WriteAllBytes(request.Arguments[7], Image(width, height, display == LiveDisplay));
                    return Result();
                default: throw new InvalidOperationException("Unexpected simulator command " + command);
            }
        }

        private ProcessRunResult FoldToolCommand(IReadOnlyList<string> arguments)
        {
            FoldToolCalls.Add(string.Join(" ", arguments));
            if (arguments[0] == "--version") return Result(output: FoldToolVersion + "\n");
            if (arguments[2] is "open" or "close") LiveDisplay = arguments[2] == "open" ? "primary-1" : "primary";
            return Result(FoldExitCode);
        }

        private byte[] Image(int width, int height, bool lit)
        {
            var key = $"{width}x{height}:{lit}";
            if (!_images.TryGetValue(key, out var bytes)) _images[key] = bytes = Png(width, height, lit, filter: 1);
            return bytes;
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
