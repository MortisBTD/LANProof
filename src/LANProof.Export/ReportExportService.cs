using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LANProof.Core.Diagnostics;
using LANProof.Core.Interfaces;
using LANProof.Core.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LANProof.Export;

public sealed class ReportExportService : IExportService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    };

    public Task<string> ExportToJsonAsync(DiagnosticReport report, string filePath, CancellationToken ct = default) =>
        ExportToJsonAsync(report, AsAsync(report.SupportingMeasurements), AsAsync(report.RelatedEvents), filePath, ct,report.MeasurementsAreSampled);

    public Task<string> ExportToJsonAsync(DiagnosticReport report, IAsyncEnumerable<ProbeResult> measurements,
        IAsyncEnumerable<NetworkEvent> events, string filePath, CancellationToken ct=default,bool measurementsAreSampled=false) => AtomicWriteAsync(filePath, async stream =>
    {
        using var writer=new Utf8JsonWriter(stream,new JsonWriterOptions { Indented=true });
        writer.WriteStartObject();
        var metadata=JsonSerializer.SerializeToElement(report,JsonOptions);
        foreach (var property in metadata.EnumerateObject())
        {
            if (property.Name is nameof(report.SupportingMeasurements) or nameof(report.RelatedEvents) or nameof(report.MeasurementsAreSampled)) continue;
            property.WriteTo(writer);
        }
        writer.WriteBoolean(nameof(report.MeasurementsAreSampled),measurementsAreSampled);
        writer.WriteStartArray(nameof(report.SupportingMeasurements)); int count=0;
        await foreach (var p in measurements.WithCancellation(ct))
        {
            JsonSerializer.Serialize(writer,p,JsonOptions);
            if (++count%256==0) await writer.FlushAsync(ct);
        }
        writer.WriteEndArray(); writer.WriteStartArray(nameof(report.RelatedEvents));
        await foreach (var e in events.WithCancellation(ct))
        {
            JsonSerializer.Serialize(writer,e,JsonOptions);
            if (++count%256==0) await writer.FlushAsync(ct);
        }
        writer.WriteEndArray(); writer.WriteEndObject(); await writer.FlushAsync(ct);
    },ct);

    public Task<string> ExportToCsvAsync(DiagnosticReport report, string filePath, CancellationToken ct = default) =>
        ExportToCsvAsync(report,AsAsync(report.SupportingMeasurements),AsAsync(report.RelatedEvents),filePath,ct);

    public Task<string> ExportToCsvAsync(DiagnosticReport report, IAsyncEnumerable<ProbeResult> measurements,
        IAsyncEnumerable<NetworkEvent> events, string filePath, CancellationToken ct=default) => AtomicWriteAsync(filePath,async stream =>
    {
        await using var writer=new StreamWriter(stream,new UTF8Encoding(true),65536,leaveOpen:true);
        Task Row(params object?[] values)=>writer.WriteLineAsync(string.Join(",",values.Select(CsvCell))).WaitAsync(ct);
        await writer.WriteLineAsync("record_type,timestamp_utc,device,probe,target,success,is_conclusive,icmp_rtt_ms,dns_resolution_ms,http_headers_ms,jitter_ms,failed_percent,details,layer,severity,end_utc,title");
        await Row("report",report.GeneratedAt.ToString("O"),"","","","","","","","","","",
            $"success_rate_percent={report.SuccessRatePercent?.ToString(CultureInfo.InvariantCulture)}; icmp_batch_loss_percent={report.IcmpPacketLossPercent?.ToString(CultureInfo.InvariantCulture)}; " + report.DetailedAnalysis,
            report.ProbableLayer,"",report.PeriodEnd.ToString("O"),report.Summary);
        await foreach (var p in measurements.WithCancellation(ct))
            await Row("measurement",p.Timestamp.ToString("O"),p.DeviceId,p.ProbeType,p.Target,p.IsSuccess,p.IsConclusive,
                p.ProbeType is ProbeType.Gateway or ProbeType.External ? p.RoundTripMs : null,p.DnsResolveMs,p.ProbeType==ProbeType.Http ? p.RoundTripMs : null,
                p.JitterMs,p.PacketLossPercent,p.Details,EventDetector.Layer(p.ProbeType),"","","");
        await foreach (var e in events.WithCancellation(ct))
            await Row("event",e.StartTime.ToString("O"),e.DeviceId,"","","","","","","","","",e.Description,e.Layer,e.Severity,e.EndTime?.ToString("O"),e.Title);
        await writer.FlushAsync(ct);
    },ct);

    private static async IAsyncEnumerable<T> AsAsync<T>(IEnumerable<T> items)
    { foreach (var item in items) yield return item; await Task.CompletedTask; }

    private static async Task<string> AtomicWriteAsync(string path,Func<Stream,Task> write,CancellationToken ct)
    {
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            await using (var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous)) await write(stream);
            ct.ThrowIfCancellationRequested(); File.Move(temporary,path,true); return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string CsvCell(object? value)
    {
        var str = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (value is string && str.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@') str = "'" + str;
        return "\"" + str.Replace("\"", "\"\"") + "\"";
    }

    public async Task<string> ExportToPdfAsync(DiagnosticReport report, string filePath, CancellationToken ct = default)
    {
        // LANProof is an MIT open-source project, eligible for the QuestPDF Community license.
        QuestPDF.Settings.License = LicenseType.Community;
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            Document.Create(document => document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10).FontColor("243347"));
                page.Header().PaddingBottom(18).Row(row =>
                {
                    row.RelativeItem().Text("LANProof").FontSize(26).Bold().FontColor("087F75");
                    row.AutoItem().AlignBottom().Text("ОТЧЁТ О СОЕДИНЕНИИ").FontSize(9);
                });
                page.Content().Column(column =>
                {
                    column.Spacing(12);
                    column.Item().Text($"Период UTC: {report.PeriodStart:yyyy-MM-dd HH:mm:ss} — {report.PeriodEnd:yyyy-MM-dd HH:mm:ss}");
                    column.Item().Text("Устройства: " + string.Join(", ", report.SupportingMeasurements.Select(p => p.DeviceId).Distinct()));
                    column.Item().Background(report.TotalMeasurements==0 ? "EDF1F6" : report.HasAnomalies ? "FFF1E5" : "E9F5F2").Padding(14).Column(box =>
                    {
                        box.Spacing(8);
                        box.Item().Text(report.Summary).FontSize(16).Bold();
                        box.Item().Text($"Успешные завершённые проверки: {Percent(report.SuccessRatePercent)}. " + (report.ProbableLayer.HasValue
                            ? $"Достоверность гипотезы: {report.ConfidencePercent}%." : report.TotalMeasurements>0 && !report.HasAnomalies ? "Отказов не обнаружено." : "Данных для локализации причины недостаточно."));
                        box.Item().Text($"Средняя потеря ICMP по партиям: шлюз — {Percent(report.GatewayPacketLossPercent)}, внешние IP — {Percent(report.ExternalPacketLossPercent)}.");
                    });
                    column.Item().Text(report.DetailedAnalysis);
                    column.Item().Text("Что проверить дальше").Bold().FontSize(13);
                    foreach (var advice in report.Recommendations) column.Item().Text("• " + advice);
                    column.Item().Text("Сводка по уровням").Bold().FontSize(13);
                    foreach (var summary in report.ProbeSummaries)
                        column.Item().Text($"{EventDetector.Label(summary.ProbeType)}: завершённых проверок — {summary.Count}, неудачных — {summary.Failures}.");
                    column.Item().Text($"События: {report.TotalEvents} (показаны последние 100)").Bold().FontSize(13);
                    if (report.TotalEvents==0) column.Item().Text("За выбранный период сетевых событий не зафиксировано.").FontColor("627086");
                    foreach (var e in report.RelatedEvents.OrderByDescending(x => x.StartTime).Take(100))
                        column.Item().Text($"{e.StartTime:MM-dd HH:mm:ss} UTC · {e.DeviceId} · {e.Title}\n{e.Description}\n" +
                            (e.EndTime is { } end ? $"Конец наблюдения: {end:HH:mm:ss} UTC" : "Последнее состояние — активно"));
                    column.Item().Text("Последние измерения (до 200)").Bold().FontSize(13);
                    column.Item().Text("Полные исходные данные доступны в JSON/CSV. Время — UTC. Для ICMP измеряется RTT, для DNS — разрешение имени, для HTTPS — ответ до заголовков, включая обработку сервиса и холодное соединение. Эти величины не взаимозаменяемы.").FontSize(9);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(1); });
                        table.Header(h => { foreach (var label in new[] { "Время", "Проверка", "Успех", "мс", "% сбоев" }) h.Cell().Padding(4).Text(label).Bold(); });
                        foreach (var p in report.SupportingMeasurements.OrderByDescending(x => x.Timestamp).Take(200))
                            foreach (var cell in new[] { p.Timestamp.ToString("MM-dd HH:mm:ss"), EventDetector.Label(p.ProbeType), !p.IsConclusive ? "?" : p.IsSuccess ? "Да" : "Нет", p.RoundTripMs?.ToString("F1") ?? "—", p.PacketLossPercent?.ToString("F0") ?? "—" })
                                table.Cell().BorderBottom(0.5f).BorderColor("DFE5EA").Padding(4).Text(cell).FontSize(9);
                    });
                    column.Item().Text("Диагностические выводы являются гипотезами. Данные собраны активными проверками; содержимое пользовательского трафика не перехватывается.").FontSize(9);
                });
                page.Footer().AlignRight().Text(t => { t.Span("LANProof 0.1 · "); t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); });
            })).GeneratePdf(filePath);
        }, ct);
        return filePath;
    }
    private static string Percent(double? value)=>value.HasValue ? $"{value.Value:F1}%" : "нет данных";
}
