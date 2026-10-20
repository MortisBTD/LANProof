namespace LANProof.Core.Interfaces;

using LANProof.Core.Models;

/// <summary>
/// Exports diagnostic reports to various formats.
/// </summary>
public interface IExportService
{
    /// <summary>Export a report to JSON format.</summary>
    Task<string> ExportToJsonAsync(DiagnosticReport report, string filePath, CancellationToken ct = default);

    /// <summary>Export a report to CSV format.</summary>
    Task<string> ExportToCsvAsync(DiagnosticReport report, string filePath, CancellationToken ct = default);

    /// <summary>Export a report to PDF format.</summary>
    Task<string> ExportToPdfAsync(DiagnosticReport report, string filePath, CancellationToken ct = default);
}
