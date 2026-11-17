using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

var mode = ReadOption(args, "--mode", args.FirstOrDefault() ?? "normal").ToLowerInvariant();
var durationSeconds = ReadIntOption(args, "--duration", 300);
var requestedPort = ReadIntOption(args, "--port", 0);

using var cancellation = new CancellationTokenSource();
if (durationSeconds > 0)
{
    cancellation.CancelAfter(TimeSpan.FromSeconds(durationSeconds));
}

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

Console.WriteLine($"Simulador académico iniciado. Modo={mode}. No ejecuta malware ni minería real.");

try
{
    switch (mode)
    {
        case "miner":
        case "mineria":
            await RunCpuLoadAsync(cancellation.Token);
            break;
        case "botnet":
            await RunLocalPeriodicActivityAsync(requestedPort, cancellation.Token);
            break;
        case "legitimate":
        case "legitima":
            await RunLegitimateTemporaryLoadAsync(cancellation.Token);
            break;
        case "persistent":
        case "persistente":
            await RunPersistentProcessAsync(requestedPort, cancellation.Token);
            break;
        case "suspicious-path":
        case "ruta-sospechosa":
            await RunSuspiciousPathObservationAsync(cancellation.Token);
            break;
        case "combined":
        case "combinado":
            await RunCombinedAsync(requestedPort, cancellation.Token);
            break;
        default:
            await RunNormalObservationAsync(cancellation.Token);
            break;
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Simulación detenida de forma controlada.");
}

// ejecuta observacion sin carga artificial
static async Task RunNormalObservationAsync(CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        Console.WriteLine($"{DateTime.Now:O} Observación normal sin carga artificial.");
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
    }
}

// ejecuta carga legitima con pausas
static async Task RunLegitimateTemporaryLoadAsync(CancellationToken cancellationToken)
{
    Console.WriteLine("Generando carga legítima temporal con descansos para evitar condición sostenida.");

    while (!cancellationToken.IsCancellationRequested)
    {
        var burst = Stopwatch.StartNew();
        while (burst.Elapsed < TimeSpan.FromSeconds(8) && !cancellationToken.IsCancellationRequested)
        {
            var value = 0d;
            for (var i = 0; i < 20_000; i++)
            {
                value += Math.Sqrt(i + value);
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
    }
}

// mantiene un proceso persistente controlado
static async Task RunPersistentProcessAsync(int requestedPort, CancellationToken cancellationToken)
{
    Console.WriteLine("Proceso persistente simulado con actividad local controlada.");
    await RunLocalPeriodicActivityAsync(requestedPort, cancellationToken);
}

// mantiene activa la prueba de ruta sospechosa
static async Task RunSuspiciousPathObservationAsync(CancellationToken cancellationToken)
{
    Console.WriteLine("Escenario de nombre o ruta no estándar en ejecución controlada.");
    while (!cancellationToken.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
    }
}

// genera carga cpu controlada
static async Task RunCpuLoadAsync(CancellationToken cancellationToken)
{
    var workerCount = Math.Max(1, Environment.ProcessorCount / 2);
    Console.WriteLine($"Generando carga CPU controlada con {workerCount} worker(s).");

    var tasks = Enumerable.Range(0, workerCount)
        .Select(_ => Task.Run(() => CpuWorker(cancellationToken), cancellationToken))
        .ToArray();

    await Task.WhenAll(tasks);
}

// genera actividad local periodica
static async Task RunLocalPeriodicActivityAsync(int requestedPort, CancellationToken cancellationToken)
{
    using var listener = CreateLoopbackListener(requestedPort);
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    Console.WriteLine($"Simulando actividad periódica local en 127.0.0.1:{port}.");

    await RunLoopbackHeartbeatAsync(listener, port, cancellationToken);
}

// combina cpu y actividad local
static async Task RunCombinedAsync(int requestedPort, CancellationToken cancellationToken)
{
    using var listener = CreateLoopbackListener(requestedPort);
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    Console.WriteLine($"Escenario combinado: CPU controlada y actividad localhost en puerto {port}.");

    var workerCount = Math.Max(1, Environment.ProcessorCount / 2);
    var cpuTasks = Enumerable.Range(0, workerCount)
        .Select(_ => Task.Run(() => CpuWorker(cancellationToken), cancellationToken));

    var allTasks = cpuTasks
        .Append(RunLoopbackHeartbeatAsync(listener, port, cancellationToken))
        .ToArray();

    await Task.WhenAll(allTasks);
}

// ejecuta trabajo controlado de cpu
static void CpuWorker(CancellationToken cancellationToken)
{
    var value = 0d;
    while (!cancellationToken.IsCancellationRequested)
    {
        var cycle = Stopwatch.StartNew();
        while (cycle.ElapsedMilliseconds < 200 && !cancellationToken.IsCancellationRequested)
        {
            value += Math.Sqrt(value + 123.456);
            if (value > 1_000_000)
            {
                value = 0;
            }
        }

        Thread.Sleep(50);
    }
}

// crea un listener local
static TcpListener CreateLoopbackListener(int requestedPort)
{
    try
    {
        var listener = new TcpListener(IPAddress.Loopback, requestedPort);
        listener.Start();
        return listener;
    }
    catch when (requestedPort != 0)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return listener;
    }
}

// envia actividad periodica por localhost
static async Task RunLoopbackHeartbeatAsync(TcpListener listener, int port, CancellationToken cancellationToken)
{
    const int connectionPairCount = 2;
    var acceptTasks = Enumerable.Range(0, connectionPairCount)
        .Select(_ => listener.AcceptTcpClientAsync(cancellationToken).AsTask())
        .ToArray();
    var clients = new List<TcpClient>();

    try
    {
        for (var index = 0; index < connectionPairCount; index++)
        {
            var client = new TcpClient();
            clients.Add(client);
            await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
        }

        var acceptedClients = await Task.WhenAll(acceptTasks);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var payload = Encoding.UTF8.GetBytes($"periodic-ping {DateTime.Now:O}");
                foreach (var client in clients)
                {
                    await client.GetStream().WriteAsync(payload, cancellationToken);
                }

                Console.WriteLine($"{DateTime.Now:O} Solicitud periódica local enviada.");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
        finally
        {
            foreach (var acceptedClient in acceptedClients)
            {
                acceptedClient.Dispose();
            }
        }
    }
    finally
    {
        foreach (var client in clients)
        {
            client.Dispose();
        }
    }
}

// lee una opcion de texto
static string ReadOption(string[] args, string optionName, string defaultValue)
{
    var prefix = optionName + "=";
    var value = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    return value is null ? defaultValue : value[prefix.Length..];
}

// lee una opcion numerica
static int ReadIntOption(string[] args, string optionName, int defaultValue)
{
    var text = ReadOption(args, optionName, defaultValue.ToString());
    return int.TryParse(text, out var value) ? value : defaultValue;
}
