using System.Globalization;
using System.IO;
using BotnetMiningDetector.App.Data.Models;
using Microsoft.Data.Sqlite;

namespace BotnetMiningDetector.App.Data;

// repositorio local de sqlite
public sealed class SqliteRepository
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    // prepara la ruta de la base local
    public SqliteRepository(string databasePath)
    {
        if (Path.IsPathRooted(databasePath))
        {
            _databasePath = databasePath;
        }
        else
        {
            var projectRoot = FindProjectRoot() ?? AppContext.BaseDirectory;
            _databasePath = Path.Combine(projectRoot, databasePath);

            var legacyDatabasePaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, databasePath),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AgenteLocalBYC",
                    databasePath)
            };
            var legacyDatabasePath = legacyDatabasePaths.FirstOrDefault(File.Exists);
            if (!File.Exists(_databasePath) && legacyDatabasePath is not null)
            {
                Directory.CreateDirectory(projectRoot);
                File.Copy(legacyDatabasePath, _databasePath);
            }
        }

        _connectionString = $"Data Source={_databasePath}";
    }

    public string DatabasePath => _databasePath;

    // busca la raiz del proyecto
    private static string? FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BotnetMiningDetector.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    // crea tablas y aplica cambios pendientes
    public async Task InitializeAsync()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var commands = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS SystemMetric (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                CpuTotalPercent REAL NOT NULL,
                MemoryUsedMb REAL NOT NULL,
                MemoryTotalMb REAL NOT NULL,
                MemoryUsedPercent REAL NOT NULL,
                MonitoringStatus TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS ProcessMetric (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                ProcessId INTEGER NOT NULL,
                ProcessName TEXT NOT NULL,
                ExecutablePath TEXT NOT NULL,
                CpuPercent REAL NOT NULL,
                MemoryMb REAL NOT NULL,
                StartTime TEXT NULL,
                LifetimeSeconds REAL NULL,
                NetworkConnectionCount INTEGER NOT NULL,
                IsKnownProcess INTEGER NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS BaselineProfile (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                CpuAverage REAL NOT NULL,
                CpuStdDev REAL NOT NULL,
                CpuMin REAL NOT NULL DEFAULT 0,
                CpuMax REAL NOT NULL DEFAULT 0,
                MemoryAverage REAL NOT NULL,
                MemoryStdDev REAL NOT NULL,
                MemoryMin REAL NOT NULL DEFAULT 0,
                MemoryMax REAL NOT NULL DEFAULT 0,
                NetworkConnectionAverage REAL NOT NULL DEFAULT 0,
                NetworkConnectionStdDev REAL NOT NULL DEFAULT 0,
                LearningDurationMinutes INTEGER NOT NULL,
                SampleCount INTEGER NOT NULL DEFAULT 0,
                ExpectedSampleCount INTEGER NOT NULL DEFAULT 0,
                SampleCoveragePercent REAL NOT NULL DEFAULT 0,
                MaximumSampleGapSeconds REAL NOT NULL DEFAULT 0,
                Status TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS ProcessBaseline (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProcessName TEXT NOT NULL,
                AverageCpu REAL NOT NULL,
                StdDevCpu REAL NOT NULL,
                AverageMemory REAL NOT NULL,
                StdDevMemory REAL NOT NULL,
                AverageNetworkConnections REAL NOT NULL DEFAULT 0,
                StdDevNetworkConnections REAL NOT NULL DEFAULT 0,
                TypicalLifetimeSeconds REAL NOT NULL,
                OccurrenceCount INTEGER NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS Alert (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                Severity TEXT NOT NULL,
                AlertType TEXT NOT NULL,
                ProcessId INTEGER NULL,
                ProcessName TEXT NOT NULL,
                MetricObserved TEXT NOT NULL,
                BaselineReference TEXT NOT NULL,
                Description TEXT NOT NULL,
                Recommendation TEXT NOT NULL,
                ObservedValue REAL NULL,
                BaselineAverage REAL NULL,
                BaselineStdDev REAL NULL,
                StdDevMultiplier REAL NULL,
                CalculatedThreshold REAL NULL,
                AbsoluteThreshold REAL NULL,
                ThresholdType TEXT NOT NULL DEFAULT 'Heuristic',
                SustainedDurationSeconds REAL NULL,
                RuleName TEXT NOT NULL DEFAULT '',
                RuleCategory TEXT NOT NULL DEFAULT '',
                EvidenceDetails TEXT NOT NULL DEFAULT '',
                WasAcknowledged INTEGER NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS ValidationScenario (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ScenarioType TEXT NOT NULL,
                ScenarioCategory TEXT NOT NULL DEFAULT '',
                Description TEXT NOT NULL DEFAULT '',
                ProcessId INTEGER NULL,
                ProcessName TEXT NOT NULL DEFAULT '',
                StartedAt TEXT NOT NULL,
                FinishedAt TEXT NULL,
                DurationSeconds REAL NULL,
                ExpectedResult TEXT NOT NULL,
                ActualResult TEXT NOT NULL,
                ExpectedClassification TEXT NOT NULL DEFAULT '',
                ActualClassification TEXT NOT NULL DEFAULT '',
                ConfusionResult TEXT NOT NULL DEFAULT '',
                AlertGenerated INTEGER NOT NULL,
                DetectionTimeSeconds REAL NULL,
                TelemetrySampleCount INTEGER NOT NULL DEFAULT 0,
                AverageCpuPercent REAL NULL,
                MaximumCpuPercent REAL NULL,
                AverageMemoryMb REAL NULL,
                MaximumMemoryMb REAL NULL,
                AverageNetworkConnections REAL NULL,
                MaximumNetworkConnections INTEGER NULL,
                TriggeredRuleName TEXT NOT NULL DEFAULT '',
                ObservedActivity TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS EvaluationSummary (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                Timestamp TEXT NOT NULL,
                TruePositives INTEGER NOT NULL,
                FalsePositives INTEGER NOT NULL,
                FalseNegatives INTEGER NOT NULL,
                TrueNegatives INTEGER NOT NULL,
                Precision REAL NOT NULL,
                Recall REAL NOT NULL,
                FalsePositiveRate REAL NOT NULL,
                AverageDetectionTimeSeconds REAL NOT NULL,
                AgentAverageCpuPercent REAL NOT NULL,
                AgentAverageMemoryMb REAL NOT NULL,
                AgentMaximumMemoryMb REAL NOT NULL DEFAULT 0,
                BaselineCoverage REAL NOT NULL,
                AlertClarityScore REAL NOT NULL,
                StatisticalActivationCount INTEGER NOT NULL,
                HeuristicActivationCount INTEGER NOT NULL,
                CorrelationActivationCount INTEGER NOT NULL,
                AbsoluteActivationCount INTEGER NOT NULL
            );
            """,
            "CREATE INDEX IF NOT EXISTS IX_SystemMetric_Timestamp ON SystemMetric(Timestamp);",
            "CREATE INDEX IF NOT EXISTS IX_ProcessMetric_Timestamp ON ProcessMetric(Timestamp);",
            "CREATE INDEX IF NOT EXISTS IX_ProcessMetric_ProcessName ON ProcessMetric(ProcessName);",
            "CREATE INDEX IF NOT EXISTS IX_Alert_Timestamp ON Alert(Timestamp);",
            "CREATE INDEX IF NOT EXISTS IX_ValidationScenario_StartedAt ON ValidationScenario(StartedAt);"
        };

        foreach (var sql in commands)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        await EnsureBaselineColumnsAsync(connection);
        await EnsureValidationScenarioColumnsAsync(connection);
        await EnsureAlertEvidenceColumnsAsync(connection);
        await EnsureEvaluationSummaryColumnsAsync(connection);
    }

    // guarda una medicion global
    public async Task InsertSystemMetricAsync(SystemMetric metric, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO SystemMetric
            (Timestamp, CpuTotalPercent, MemoryUsedMb, MemoryTotalMb, MemoryUsedPercent, MonitoringStatus)
            VALUES ($Timestamp, $CpuTotalPercent, $MemoryUsedMb, $MemoryTotalMb, $MemoryUsedPercent, $MonitoringStatus);
            """;
        Add(command, "$Timestamp", ToDbDate(metric.Timestamp));
        Add(command, "$CpuTotalPercent", metric.CpuTotalPercent);
        Add(command, "$MemoryUsedMb", metric.MemoryUsedMb);
        Add(command, "$MemoryTotalMb", metric.MemoryTotalMb);
        Add(command, "$MemoryUsedPercent", metric.MemoryUsedPercent);
        Add(command, "$MonitoringStatus", metric.MonitoringStatus);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // guarda mediciones de procesos
    public async Task InsertProcessMetricsAsync(IEnumerable<ProcessMetric> metrics, CancellationToken cancellationToken = default)
    {
        var list = metrics.ToList();
        if (list.Count == 0)
        {
            return;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO ProcessMetric
            (Timestamp, ProcessId, ProcessName, ExecutablePath, CpuPercent, MemoryMb, StartTime, LifetimeSeconds, NetworkConnectionCount, IsKnownProcess)
            VALUES ($Timestamp, $ProcessId, $ProcessName, $ExecutablePath, $CpuPercent, $MemoryMb, $StartTime, $LifetimeSeconds, $NetworkConnectionCount, $IsKnownProcess);
            """;

        foreach (var metric in list)
        {
            command.Parameters.Clear();
            Add(command, "$Timestamp", ToDbDate(metric.Timestamp));
            Add(command, "$ProcessId", metric.ProcessId);
            Add(command, "$ProcessName", metric.ProcessName);
            Add(command, "$ExecutablePath", metric.ExecutablePath);
            Add(command, "$CpuPercent", metric.CpuPercent);
            Add(command, "$MemoryMb", metric.MemoryMb);
            Add(command, "$StartTime", metric.StartTime is null ? null : ToDbDate(metric.StartTime.Value));
            Add(command, "$LifetimeSeconds", metric.LifetimeSeconds);
            Add(command, "$NetworkConnectionCount", metric.NetworkConnectionCount);
            Add(command, "$IsKnownProcess", metric.IsKnownProcess ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // guarda una alerta
    public async Task<long> InsertAlertAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Alert
            (Timestamp, Severity, AlertType, ProcessId, ProcessName, MetricObserved, BaselineReference, Description, Recommendation,
             ObservedValue, BaselineAverage, BaselineStdDev, StdDevMultiplier, CalculatedThreshold, AbsoluteThreshold, ThresholdType,
             SustainedDurationSeconds, RuleName, RuleCategory, EvidenceDetails, WasAcknowledged)
            VALUES ($Timestamp, $Severity, $AlertType, $ProcessId, $ProcessName, $MetricObserved, $BaselineReference, $Description, $Recommendation,
             $ObservedValue, $BaselineAverage, $BaselineStdDev, $StdDevMultiplier, $CalculatedThreshold, $AbsoluteThreshold, $ThresholdType,
             $SustainedDurationSeconds, $RuleName, $RuleCategory, $EvidenceDetails, $WasAcknowledged);
            SELECT last_insert_rowid();
            """;
        Add(command, "$Timestamp", ToDbDate(alert.Timestamp));
        Add(command, "$Severity", alert.Severity);
        Add(command, "$AlertType", alert.AlertType);
        Add(command, "$ProcessId", alert.ProcessId);
        Add(command, "$ProcessName", alert.ProcessName);
        Add(command, "$MetricObserved", alert.MetricObserved);
        Add(command, "$BaselineReference", alert.BaselineReference);
        Add(command, "$Description", alert.Description);
        Add(command, "$Recommendation", alert.Recommendation);
        Add(command, "$ObservedValue", alert.ObservedValue);
        Add(command, "$BaselineAverage", alert.BaselineAverage);
        Add(command, "$BaselineStdDev", alert.BaselineStdDev);
        Add(command, "$StdDevMultiplier", alert.StdDevMultiplier);
        Add(command, "$CalculatedThreshold", alert.CalculatedThreshold);
        Add(command, "$AbsoluteThreshold", alert.AbsoluteThreshold);
        Add(command, "$ThresholdType", alert.ThresholdType);
        Add(command, "$SustainedDurationSeconds", alert.SustainedDurationSeconds);
        Add(command, "$RuleName", alert.RuleName);
        Add(command, "$RuleCategory", alert.RuleCategory);
        Add(command, "$EvidenceDetails", alert.EvidenceDetails);
        Add(command, "$WasAcknowledged", alert.WasAcknowledged ? 1 : 0);
        var id = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        alert.Id = id;
        return id;
    }

    // marca una alerta como revisada
    public async Task AcknowledgeAlertAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Alert SET WasAcknowledged = 1 WHERE Id = $Id;";
        Add(command, "$Id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // reemplaza la linea base activa
    public async Task ReplaceBaselineAsync(BaselineProfile profile, IReadOnlyCollection<ProcessBaseline> processes, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var archiveCommand = connection.CreateCommand())
        {
            archiveCommand.Transaction = transaction;
            archiveCommand.CommandText = "UPDATE BaselineProfile SET Status = 'Archived', UpdatedAt = $UpdatedAt WHERE Status = 'Active';";
            Add(archiveCommand, "$UpdatedAt", ToDbDate(DateTime.Now));
            await archiveCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var clearCommand = connection.CreateCommand())
        {
            clearCommand.Transaction = transaction;
            clearCommand.CommandText = "DELETE FROM ProcessBaseline;";
            await clearCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var profileCommand = connection.CreateCommand())
        {
            profileCommand.Transaction = transaction;
            profileCommand.CommandText =
                """
                INSERT INTO BaselineProfile
                (CreatedAt, UpdatedAt, CpuAverage, CpuStdDev, CpuMin, CpuMax, MemoryAverage, MemoryStdDev, MemoryMin, MemoryMax,
                 NetworkConnectionAverage, NetworkConnectionStdDev, LearningDurationMinutes, SampleCount, ExpectedSampleCount,
                 SampleCoveragePercent, MaximumSampleGapSeconds, Status)
                VALUES ($CreatedAt, $UpdatedAt, $CpuAverage, $CpuStdDev, $CpuMin, $CpuMax, $MemoryAverage, $MemoryStdDev, $MemoryMin, $MemoryMax,
                 $NetworkConnectionAverage, $NetworkConnectionStdDev, $LearningDurationMinutes, $SampleCount, $ExpectedSampleCount,
                 $SampleCoveragePercent, $MaximumSampleGapSeconds, $Status);
                SELECT last_insert_rowid();
                """;
            Add(profileCommand, "$CreatedAt", ToDbDate(profile.CreatedAt));
            Add(profileCommand, "$UpdatedAt", ToDbDate(profile.UpdatedAt));
            Add(profileCommand, "$CpuAverage", profile.CpuAverage);
            Add(profileCommand, "$CpuStdDev", profile.CpuStdDev);
            Add(profileCommand, "$CpuMin", profile.CpuMin);
            Add(profileCommand, "$CpuMax", profile.CpuMax);
            Add(profileCommand, "$MemoryAverage", profile.MemoryAverage);
            Add(profileCommand, "$MemoryStdDev", profile.MemoryStdDev);
            Add(profileCommand, "$MemoryMin", profile.MemoryMin);
            Add(profileCommand, "$MemoryMax", profile.MemoryMax);
            Add(profileCommand, "$NetworkConnectionAverage", profile.NetworkConnectionAverage);
            Add(profileCommand, "$NetworkConnectionStdDev", profile.NetworkConnectionStdDev);
            Add(profileCommand, "$LearningDurationMinutes", profile.LearningDurationMinutes);
            Add(profileCommand, "$SampleCount", profile.SampleCount);
            Add(profileCommand, "$ExpectedSampleCount", profile.ExpectedSampleCount);
            Add(profileCommand, "$SampleCoveragePercent", profile.SampleCoveragePercent);
            Add(profileCommand, "$MaximumSampleGapSeconds", profile.MaximumSampleGapSeconds);
            Add(profileCommand, "$Status", profile.Status);
            profile.Id = (long)(await profileCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }

        await using var processCommand = connection.CreateCommand();
        processCommand.Transaction = transaction;
        processCommand.CommandText =
            """
            INSERT INTO ProcessBaseline
            (ProcessName, AverageCpu, StdDevCpu, AverageMemory, StdDevMemory, AverageNetworkConnections, StdDevNetworkConnections, TypicalLifetimeSeconds, OccurrenceCount)
            VALUES ($ProcessName, $AverageCpu, $StdDevCpu, $AverageMemory, $StdDevMemory, $AverageNetworkConnections, $StdDevNetworkConnections, $TypicalLifetimeSeconds, $OccurrenceCount);
            """;

        foreach (var process in processes)
        {
            processCommand.Parameters.Clear();
            Add(processCommand, "$ProcessName", process.ProcessName);
            Add(processCommand, "$AverageCpu", process.AverageCpu);
            Add(processCommand, "$StdDevCpu", process.StdDevCpu);
            Add(processCommand, "$AverageMemory", process.AverageMemory);
            Add(processCommand, "$StdDevMemory", process.StdDevMemory);
            Add(processCommand, "$AverageNetworkConnections", process.AverageNetworkConnections);
            Add(processCommand, "$StdDevNetworkConnections", process.StdDevNetworkConnections);
            Add(processCommand, "$TypicalLifetimeSeconds", process.TypicalLifetimeSeconds);
            Add(processCommand, "$OccurrenceCount", process.OccurrenceCount);
            await processCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // registra un escenario de validacion
    public async Task<long> InsertValidationScenarioAsync(ValidationScenario scenario, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ValidationScenario
            (Name, ScenarioType, ScenarioCategory, Description, ProcessId, ProcessName, StartedAt, FinishedAt, DurationSeconds,
             ExpectedResult, ActualResult, ExpectedClassification, ActualClassification, ConfusionResult,
             AlertGenerated, DetectionTimeSeconds, TelemetrySampleCount, AverageCpuPercent, MaximumCpuPercent,
             AverageMemoryMb, MaximumMemoryMb, AverageNetworkConnections, MaximumNetworkConnections,
             TriggeredRuleName, ObservedActivity, Notes)
            VALUES ($Name, $ScenarioType, $ScenarioCategory, $Description, $ProcessId, $ProcessName, $StartedAt, $FinishedAt, $DurationSeconds,
             $ExpectedResult, $ActualResult, $ExpectedClassification, $ActualClassification, $ConfusionResult,
             $AlertGenerated, $DetectionTimeSeconds, $TelemetrySampleCount, $AverageCpuPercent, $MaximumCpuPercent,
             $AverageMemoryMb, $MaximumMemoryMb, $AverageNetworkConnections, $MaximumNetworkConnections,
             $TriggeredRuleName, $ObservedActivity, $Notes);
            SELECT last_insert_rowid();
            """;
        Add(command, "$Name", scenario.Name);
        Add(command, "$ScenarioType", scenario.ScenarioType);
        Add(command, "$ScenarioCategory", scenario.ScenarioCategory);
        Add(command, "$Description", scenario.Description);
        Add(command, "$ProcessId", scenario.ProcessId);
        Add(command, "$ProcessName", scenario.ProcessName);
        Add(command, "$StartedAt", ToDbDate(scenario.StartedAt));
        Add(command, "$FinishedAt", scenario.FinishedAt is null ? null : ToDbDate(scenario.FinishedAt.Value));
        Add(command, "$DurationSeconds", scenario.DurationSeconds);
        Add(command, "$ExpectedResult", scenario.ExpectedResult);
        Add(command, "$ActualResult", scenario.ActualResult);
        Add(command, "$ExpectedClassification", scenario.ExpectedClassification);
        Add(command, "$ActualClassification", scenario.ActualClassification);
        Add(command, "$ConfusionResult", scenario.ConfusionResult);
        Add(command, "$AlertGenerated", scenario.AlertGenerated ? 1 : 0);
        Add(command, "$DetectionTimeSeconds", scenario.DetectionTimeSeconds);
        AddScenarioTelemetryParameters(command, scenario);
        Add(command, "$Notes", scenario.Notes);
        var id = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        scenario.Id = id;
        return id;
    }

    // actualiza un escenario de validacion
    public async Task UpdateValidationScenarioAsync(ValidationScenario scenario, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE ValidationScenario
            SET FinishedAt = $FinishedAt,
                DurationSeconds = $DurationSeconds,
                ProcessId = $ProcessId,
                ProcessName = $ProcessName,
                ActualResult = $ActualResult,
                ActualClassification = $ActualClassification,
                ConfusionResult = $ConfusionResult,
                AlertGenerated = $AlertGenerated,
                DetectionTimeSeconds = $DetectionTimeSeconds,
                TelemetrySampleCount = $TelemetrySampleCount,
                AverageCpuPercent = $AverageCpuPercent,
                MaximumCpuPercent = $MaximumCpuPercent,
                AverageMemoryMb = $AverageMemoryMb,
                MaximumMemoryMb = $MaximumMemoryMb,
                AverageNetworkConnections = $AverageNetworkConnections,
                MaximumNetworkConnections = $MaximumNetworkConnections,
                TriggeredRuleName = $TriggeredRuleName,
                ObservedActivity = $ObservedActivity,
                Notes = $Notes
            WHERE Id = $Id;
            """;
        Add(command, "$FinishedAt", scenario.FinishedAt is null ? null : ToDbDate(scenario.FinishedAt.Value));
        Add(command, "$DurationSeconds", scenario.DurationSeconds);
        Add(command, "$ProcessId", scenario.ProcessId);
        Add(command, "$ProcessName", scenario.ProcessName);
        Add(command, "$ActualResult", scenario.ActualResult);
        Add(command, "$ActualClassification", scenario.ActualClassification);
        Add(command, "$ConfusionResult", scenario.ConfusionResult);
        Add(command, "$AlertGenerated", scenario.AlertGenerated ? 1 : 0);
        Add(command, "$DetectionTimeSeconds", scenario.DetectionTimeSeconds);
        AddScenarioTelemetryParameters(command, scenario);
        Add(command, "$Notes", scenario.Notes);
        Add(command, "$Id", scenario.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // limpia datos temporales de experimentos
    public async Task ClearExperimentDataAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var table in new[] { "SystemMetric", "ProcessMetric", "Alert", "ValidationScenario", "EvaluationSummary" })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {table};";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // guarda el resumen de evaluacion
    public async Task SaveEvaluationSummaryAsync(EvaluationSummary summary, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO EvaluationSummary
            (Id, Timestamp, TruePositives, FalsePositives, FalseNegatives, TrueNegatives, Precision, Recall, FalsePositiveRate,
             AverageDetectionTimeSeconds, AgentAverageCpuPercent, AgentAverageMemoryMb, AgentMaximumMemoryMb, BaselineCoverage, AlertClarityScore,
             StatisticalActivationCount, HeuristicActivationCount, CorrelationActivationCount, AbsoluteActivationCount)
            VALUES
            (1, $Timestamp, $TruePositives, $FalsePositives, $FalseNegatives, $TrueNegatives, $Precision, $Recall, $FalsePositiveRate,
             $AverageDetectionTimeSeconds, $AgentAverageCpuPercent, $AgentAverageMemoryMb, $AgentMaximumMemoryMb, $BaselineCoverage, $AlertClarityScore,
             $StatisticalActivationCount, $HeuristicActivationCount, $CorrelationActivationCount, $AbsoluteActivationCount)
            ON CONFLICT(Id) DO UPDATE SET
                Timestamp = excluded.Timestamp,
                TruePositives = excluded.TruePositives,
                FalsePositives = excluded.FalsePositives,
                FalseNegatives = excluded.FalseNegatives,
                TrueNegatives = excluded.TrueNegatives,
                Precision = excluded.Precision,
                Recall = excluded.Recall,
                FalsePositiveRate = excluded.FalsePositiveRate,
                AverageDetectionTimeSeconds = excluded.AverageDetectionTimeSeconds,
                AgentAverageCpuPercent = excluded.AgentAverageCpuPercent,
                AgentAverageMemoryMb = excluded.AgentAverageMemoryMb,
                AgentMaximumMemoryMb = excluded.AgentMaximumMemoryMb,
                BaselineCoverage = excluded.BaselineCoverage,
                AlertClarityScore = excluded.AlertClarityScore,
                StatisticalActivationCount = excluded.StatisticalActivationCount,
                HeuristicActivationCount = excluded.HeuristicActivationCount,
                CorrelationActivationCount = excluded.CorrelationActivationCount,
                AbsoluteActivationCount = excluded.AbsoluteActivationCount;
            """;
        Add(command, "$Timestamp", ToDbDate(summary.Timestamp));
        Add(command, "$TruePositives", summary.TruePositives);
        Add(command, "$FalsePositives", summary.FalsePositives);
        Add(command, "$FalseNegatives", summary.FalseNegatives);
        Add(command, "$TrueNegatives", summary.TrueNegatives);
        Add(command, "$Precision", summary.Precision);
        Add(command, "$Recall", summary.Recall);
        Add(command, "$FalsePositiveRate", summary.FalsePositiveRate);
        Add(command, "$AverageDetectionTimeSeconds", summary.AverageDetectionTimeSeconds);
        Add(command, "$AgentAverageCpuPercent", summary.AgentAverageCpuPercent);
        Add(command, "$AgentAverageMemoryMb", summary.AgentAverageMemoryMb);
        Add(command, "$AgentMaximumMemoryMb", summary.AgentMaximumMemoryMb);
        Add(command, "$BaselineCoverage", summary.BaselineCoverage);
        Add(command, "$AlertClarityScore", summary.AlertClarityScore);
        Add(command, "$StatisticalActivationCount", summary.StatisticalActivationCount);
        Add(command, "$HeuristicActivationCount", summary.HeuristicActivationCount);
        Add(command, "$CorrelationActivationCount", summary.CorrelationActivationCount);
        Add(command, "$AbsoluteActivationCount", summary.AbsoluteActivationCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // obtiene la linea base activa
    public async Task<BaselineProfile?> GetActiveBaselineProfileAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, CreatedAt, UpdatedAt, CpuAverage, CpuStdDev, CpuMin, CpuMax, MemoryAverage, MemoryStdDev, MemoryMin, MemoryMax,
                   NetworkConnectionAverage, NetworkConnectionStdDev, LearningDurationMinutes, SampleCount, ExpectedSampleCount,
                   SampleCoveragePercent, MaximumSampleGapSeconds, Status
            FROM BaselineProfile
            WHERE Status = 'Active'
            ORDER BY Id DESC
            LIMIT 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadBaselineProfile(reader) : null;
    }

    // obtiene perfiles base de procesos
    public async Task<List<ProcessBaseline>> GetProcessBaselinesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ProcessBaseline>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ProcessName, AverageCpu, StdDevCpu, AverageMemory, StdDevMemory, AverageNetworkConnections, StdDevNetworkConnections,
                   TypicalLifetimeSeconds, OccurrenceCount
            FROM ProcessBaseline
            ORDER BY OccurrenceCount DESC, ProcessName;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadProcessBaseline(reader));
        }

        return result;
    }

    // obtiene alertas recientes
    public async Task<List<Alert>> GetRecentAlertsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var result = new List<Alert>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Timestamp, Severity, AlertType, ProcessId, ProcessName, MetricObserved, BaselineReference, Description, Recommendation,
                   ObservedValue, BaselineAverage, BaselineStdDev, StdDevMultiplier, CalculatedThreshold, AbsoluteThreshold,
                   ThresholdType, SustainedDurationSeconds, RuleName, RuleCategory, EvidenceDetails, WasAcknowledged
            FROM Alert
            ORDER BY Id DESC
            LIMIT $Limit;
            """;
        Add(command, "$Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadAlert(reader));
        }

        return result;
    }

    // cuenta alertas almacenadas
    public async Task<int> GetAlertCountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM Alert;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    // obtiene mediciones globales
    public async Task<List<SystemMetric>> GetSystemMetricsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<SystemMetric>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Timestamp, CpuTotalPercent, MemoryUsedMb, MemoryTotalMb, MemoryUsedPercent, MonitoringStatus
            FROM SystemMetric
            ORDER BY Id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadSystemMetric(reader));
        }

        return result;
    }

    // obtiene mediciones de procesos
    public async Task<List<ProcessMetric>> GetProcessMetricsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ProcessMetric>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Timestamp, ProcessId, ProcessName, ExecutablePath, CpuPercent, MemoryMb, StartTime, LifetimeSeconds, NetworkConnectionCount, IsKnownProcess
            FROM ProcessMetric
            ORDER BY Id;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadProcessMetric(reader));
        }

        return result;
    }

    // obtiene escenarios registrados
    public async Task<List<ValidationScenario>> GetValidationScenariosAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ValidationScenario>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, ScenarioType, ScenarioCategory, Description, ProcessId, ProcessName, StartedAt, FinishedAt, DurationSeconds,
                   ExpectedResult, ActualResult, ExpectedClassification, ActualClassification, ConfusionResult,
                   AlertGenerated, DetectionTimeSeconds, TelemetrySampleCount, AverageCpuPercent, MaximumCpuPercent,
                   AverageMemoryMb, MaximumMemoryMb, AverageNetworkConnections, MaximumNetworkConnections,
                   TriggeredRuleName, ObservedActivity, Notes
            FROM ValidationScenario
            ORDER BY Id DESC;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadValidationScenario(reader));
        }

        return result;
    }

    // abre una conexion local
    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    // asegura columnas de linea base
    private static async Task EnsureBaselineColumnsAsync(SqliteConnection connection)
    {
        await EnsureColumnsAsync(connection, "BaselineProfile", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CpuMin"] = "REAL NOT NULL DEFAULT 0",
            ["CpuMax"] = "REAL NOT NULL DEFAULT 0",
            ["MemoryMin"] = "REAL NOT NULL DEFAULT 0",
            ["MemoryMax"] = "REAL NOT NULL DEFAULT 0",
            ["NetworkConnectionAverage"] = "REAL NOT NULL DEFAULT 0",
            ["NetworkConnectionStdDev"] = "REAL NOT NULL DEFAULT 0",
            ["SampleCount"] = "INTEGER NOT NULL DEFAULT 0",
            ["ExpectedSampleCount"] = "INTEGER NOT NULL DEFAULT 0",
            ["SampleCoveragePercent"] = "REAL NOT NULL DEFAULT 0",
            ["MaximumSampleGapSeconds"] = "REAL NOT NULL DEFAULT 0"
        });

        await EnsureColumnsAsync(connection, "ProcessBaseline", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AverageNetworkConnections"] = "REAL NOT NULL DEFAULT 0",
            ["StdDevNetworkConnections"] = "REAL NOT NULL DEFAULT 0"
        });
    }

    // asegura columnas de escenarios
    private static async Task EnsureValidationScenarioColumnsAsync(SqliteConnection connection)
    {
        await EnsureColumnsAsync(connection, "ValidationScenario", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ScenarioCategory"] = "TEXT NOT NULL DEFAULT ''",
            ["Description"] = "TEXT NOT NULL DEFAULT ''",
            ["ProcessId"] = "INTEGER NULL",
            ["ProcessName"] = "TEXT NOT NULL DEFAULT ''",
            ["DurationSeconds"] = "REAL NULL",
            ["ExpectedClassification"] = "TEXT NOT NULL DEFAULT ''",
            ["ActualClassification"] = "TEXT NOT NULL DEFAULT ''",
            ["ConfusionResult"] = "TEXT NOT NULL DEFAULT ''",
            ["TelemetrySampleCount"] = "INTEGER NOT NULL DEFAULT 0",
            ["AverageCpuPercent"] = "REAL NULL",
            ["MaximumCpuPercent"] = "REAL NULL",
            ["AverageMemoryMb"] = "REAL NULL",
            ["MaximumMemoryMb"] = "REAL NULL",
            ["AverageNetworkConnections"] = "REAL NULL",
            ["MaximumNetworkConnections"] = "INTEGER NULL",
            ["TriggeredRuleName"] = "TEXT NOT NULL DEFAULT ''",
            ["ObservedActivity"] = "TEXT NOT NULL DEFAULT ''"
        });
    }

    // asegura columnas de evidencia
    private static async Task EnsureAlertEvidenceColumnsAsync(SqliteConnection connection)
    {
        var requiredColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ObservedValue"] = "REAL NULL",
            ["BaselineAverage"] = "REAL NULL",
            ["BaselineStdDev"] = "REAL NULL",
            ["StdDevMultiplier"] = "REAL NULL",
            ["CalculatedThreshold"] = "REAL NULL",
            ["AbsoluteThreshold"] = "REAL NULL",
            ["ThresholdType"] = "TEXT NOT NULL DEFAULT 'Heuristic'",
            ["SustainedDurationSeconds"] = "REAL NULL",
            ["RuleName"] = "TEXT NOT NULL DEFAULT ''",
            ["RuleCategory"] = "TEXT NOT NULL DEFAULT ''",
            ["EvidenceDetails"] = "TEXT NOT NULL DEFAULT ''"
        };

        await EnsureColumnsAsync(connection, "Alert", requiredColumns);
    }

    // asegura columnas de evaluacion
    private static async Task EnsureEvaluationSummaryColumnsAsync(SqliteConnection connection)
    {
        await EnsureColumnsAsync(connection, "EvaluationSummary", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AgentMaximumMemoryMb"] = "REAL NOT NULL DEFAULT 0"
        });
    }

    // agrega columnas que falten
    private static async Task EnsureColumnsAsync(SqliteConnection connection, string tableName, Dictionary<string, string> requiredColumns)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info({tableName});";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                existingColumns.Add(reader.GetString(1));
            }
        }

        foreach (var column in requiredColumns)
        {
            if (existingColumns.Contains(column.Key))
            {
                continue;
            }

            await using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {column.Key} {column.Value};";
            await alterCommand.ExecuteNonQueryAsync();
        }
    }

    // agrega datos de telemetria del escenario
    private static void AddScenarioTelemetryParameters(SqliteCommand command, ValidationScenario scenario)
    {
        Add(command, "$TelemetrySampleCount", scenario.TelemetrySampleCount);
        Add(command, "$AverageCpuPercent", scenario.AverageCpuPercent);
        Add(command, "$MaximumCpuPercent", scenario.MaximumCpuPercent);
        Add(command, "$AverageMemoryMb", scenario.AverageMemoryMb);
        Add(command, "$MaximumMemoryMb", scenario.MaximumMemoryMb);
        Add(command, "$AverageNetworkConnections", scenario.AverageNetworkConnections);
        Add(command, "$MaximumNetworkConnections", scenario.MaximumNetworkConnections);
        Add(command, "$TriggeredRuleName", scenario.TriggeredRuleName);
        Add(command, "$ObservedActivity", scenario.ObservedActivity);
    }

    // agrega un parametro sqlite
    private static void Add(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    // convierte una fecha para sqlite
    private static string ToDbDate(DateTime value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    // convierte una fecha desde sqlite
    private static DateTime FromDbDate(string value)
    {
        return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime();
    }

    // lee una medicion global
    private static SystemMetric ReadSystemMetric(SqliteDataReader reader)
    {
        return new SystemMetric
        {
            Id = reader.GetInt64(0),
            Timestamp = FromDbDate(reader.GetString(1)),
            CpuTotalPercent = reader.GetDouble(2),
            MemoryUsedMb = reader.GetDouble(3),
            MemoryTotalMb = reader.GetDouble(4),
            MemoryUsedPercent = reader.GetDouble(5),
            MonitoringStatus = reader.GetString(6)
        };
    }

    // lee una medicion de proceso
    private static ProcessMetric ReadProcessMetric(SqliteDataReader reader)
    {
        return new ProcessMetric
        {
            Id = reader.GetInt64(0),
            Timestamp = FromDbDate(reader.GetString(1)),
            ProcessId = reader.GetInt32(2),
            ProcessName = reader.GetString(3),
            ExecutablePath = reader.GetString(4),
            CpuPercent = reader.GetDouble(5),
            MemoryMb = reader.GetDouble(6),
            StartTime = reader.IsDBNull(7) ? null : FromDbDate(reader.GetString(7)),
            LifetimeSeconds = reader.IsDBNull(8) ? null : reader.GetDouble(8),
            NetworkConnectionCount = reader.GetInt32(9),
            IsKnownProcess = reader.GetInt32(10) == 1
        };
    }

    // lee un perfil global
    private static BaselineProfile ReadBaselineProfile(SqliteDataReader reader)
    {
        return new BaselineProfile
        {
            Id = reader.GetInt64(0),
            CreatedAt = FromDbDate(reader.GetString(1)),
            UpdatedAt = FromDbDate(reader.GetString(2)),
            CpuAverage = reader.GetDouble(3),
            CpuStdDev = reader.GetDouble(4),
            CpuMin = reader.GetDouble(5),
            CpuMax = reader.GetDouble(6),
            MemoryAverage = reader.GetDouble(7),
            MemoryStdDev = reader.GetDouble(8),
            MemoryMin = reader.GetDouble(9),
            MemoryMax = reader.GetDouble(10),
            NetworkConnectionAverage = reader.GetDouble(11),
            NetworkConnectionStdDev = reader.GetDouble(12),
            LearningDurationMinutes = reader.GetInt32(13),
            SampleCount = reader.GetInt32(14),
            ExpectedSampleCount = reader.GetInt32(15),
            SampleCoveragePercent = reader.GetDouble(16),
            MaximumSampleGapSeconds = reader.GetDouble(17),
            Status = reader.GetString(18)
        };
    }

    // lee un perfil de proceso
    private static ProcessBaseline ReadProcessBaseline(SqliteDataReader reader)
    {
        return new ProcessBaseline
        {
            Id = reader.GetInt64(0),
            ProcessName = reader.GetString(1),
            AverageCpu = reader.GetDouble(2),
            StdDevCpu = reader.GetDouble(3),
            AverageMemory = reader.GetDouble(4),
            StdDevMemory = reader.GetDouble(5),
            AverageNetworkConnections = reader.GetDouble(6),
            StdDevNetworkConnections = reader.GetDouble(7),
            TypicalLifetimeSeconds = reader.GetDouble(8),
            OccurrenceCount = reader.GetInt32(9)
        };
    }

    // lee una alerta
    private static Alert ReadAlert(SqliteDataReader reader)
    {
        return new Alert
        {
            Id = reader.GetInt64(0),
            Timestamp = FromDbDate(reader.GetString(1)),
            Severity = reader.GetString(2),
            AlertType = reader.GetString(3),
            ProcessId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            ProcessName = reader.GetString(5),
            MetricObserved = reader.GetString(6),
            BaselineReference = reader.GetString(7),
            Description = reader.GetString(8),
            Recommendation = reader.GetString(9),
            ObservedValue = reader.IsDBNull(10) ? null : reader.GetDouble(10),
            BaselineAverage = reader.IsDBNull(11) ? null : reader.GetDouble(11),
            BaselineStdDev = reader.IsDBNull(12) ? null : reader.GetDouble(12),
            StdDevMultiplier = reader.IsDBNull(13) ? null : reader.GetDouble(13),
            CalculatedThreshold = reader.IsDBNull(14) ? null : reader.GetDouble(14),
            AbsoluteThreshold = reader.IsDBNull(15) ? null : reader.GetDouble(15),
            ThresholdType = reader.GetString(16),
            SustainedDurationSeconds = reader.IsDBNull(17) ? null : reader.GetDouble(17),
            RuleName = reader.GetString(18),
            RuleCategory = reader.GetString(19),
            EvidenceDetails = reader.GetString(20),
            WasAcknowledged = reader.GetInt32(21) == 1
        };
    }

    // lee un escenario
    private static ValidationScenario ReadValidationScenario(SqliteDataReader reader)
    {
        return new ValidationScenario
        {
            Id = reader.GetInt64(0),
            Name = reader.GetString(1),
            ScenarioType = reader.GetString(2),
            ScenarioCategory = reader.GetString(3),
            Description = reader.GetString(4),
            ProcessId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            ProcessName = reader.GetString(6),
            StartedAt = FromDbDate(reader.GetString(7)),
            FinishedAt = reader.IsDBNull(8) ? null : FromDbDate(reader.GetString(8)),
            DurationSeconds = reader.IsDBNull(9) ? null : reader.GetDouble(9),
            ExpectedResult = reader.GetString(10),
            ActualResult = reader.GetString(11),
            ExpectedClassification = reader.GetString(12),
            ActualClassification = reader.GetString(13),
            ConfusionResult = reader.GetString(14),
            AlertGenerated = reader.GetInt32(15) == 1,
            DetectionTimeSeconds = reader.IsDBNull(16) ? null : reader.GetDouble(16),
            TelemetrySampleCount = reader.GetInt32(17),
            AverageCpuPercent = reader.IsDBNull(18) ? null : reader.GetDouble(18),
            MaximumCpuPercent = reader.IsDBNull(19) ? null : reader.GetDouble(19),
            AverageMemoryMb = reader.IsDBNull(20) ? null : reader.GetDouble(20),
            MaximumMemoryMb = reader.IsDBNull(21) ? null : reader.GetDouble(21),
            AverageNetworkConnections = reader.IsDBNull(22) ? null : reader.GetDouble(22),
            MaximumNetworkConnections = reader.IsDBNull(23) ? null : reader.GetInt32(23),
            TriggeredRuleName = reader.GetString(24),
            ObservedActivity = reader.GetString(25),
            Notes = reader.GetString(26)
        };
    }
}
