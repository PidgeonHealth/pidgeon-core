// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.DeIdentification;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.DeIdentification;

/// <summary>
/// Application service for estimating processing resources and time requirements.
/// Provides capacity planning for de-identification operations.
/// </summary>
internal class ResourceEstimationService
{
    // Performance constants based on benchmarking
    private const double MillisecondsPerByte = 0.001; // 1ms per KB
    private const long BytesPerMegabyte = 1024 * 1024;
    private const int EstimatedMessagesPerFile = 10;
    private const double MemoryMultiplier = 2.0; // 2x file size for processing
    private const long MinimumMemoryMb = 64; // At least 64MB
    
    /// <summary>
    /// Estimates processing time and resource requirements for a batch job.
    /// </summary>
    public async Task<Result<ProcessingEstimate>> EstimateResourcesAsync(
        string inputPath, 
        DeIdentificationOptions options)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                return Result<ProcessingEstimate>.Failure("Input path cannot be null or empty");

            if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
                return Result<ProcessingEstimate>.Failure($"Path not found: {inputPath}");

            var fileAnalysis = await AnalyzeInputFilesAsync(inputPath);
            if (fileAnalysis.IsFailure)
                return Result<ProcessingEstimate>.Failure(fileAnalysis.Error);

            var estimate = CalculateProcessingEstimate(fileAnalysis.Value, options);
            
            return Result<ProcessingEstimate>.Success(estimate);
        }
        catch (Exception ex)
        {
            return Result<ProcessingEstimate>.Failure($"Resource estimation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Estimates resources for batch processing of multiple files.
    /// </summary>
    public async Task<Result<BatchProcessingEstimate>> EstimateBatchResourcesAsync(
        IEnumerable<string> inputPaths,
        DeIdentificationOptions options)
    {
        try
        {
            var pathList = inputPaths.ToList();
            if (!pathList.Any())
                return Result<BatchProcessingEstimate>.Failure("No input paths provided");

            var totalFiles = 0;
            var totalSize = 0L;
            var fileEstimates = new List<ProcessingEstimate>();

            foreach (var path in pathList)
            {
                var estimate = await EstimateResourcesAsync(path, options);
                if (estimate.IsSuccess)
                {
                    fileEstimates.Add(estimate.Value);
                    totalFiles += estimate.Value.FileCount;
                    totalSize += estimate.Value.TotalInputSizeBytes;
                }
            }

            var batchEstimate = new BatchProcessingEstimate
            {
                TotalFiles = totalFiles,
                TotalInputSizeBytes = totalSize,
                EstimatedTotalTime = TimeSpan.FromTicks(fileEstimates.Sum(e => e.EstimatedTime.Ticks)),
                EstimatedPeakMemoryBytes = fileEstimates.Max(e => e.EstimatedMemoryBytes),
                EstimatedThroughput = totalFiles > 0 ? 
                    totalFiles / Math.Max(1, fileEstimates.Sum(e => e.EstimatedTime.TotalSeconds)) : 0,
                FileEstimates = fileEstimates,
                RecommendedBatchSize = CalculateOptimalBatchSize(totalFiles, totalSize),
                EstimateConfidence = CalculateBatchConfidence(fileEstimates)
            };

            return Result<BatchProcessingEstimate>.Success(batchEstimate);
        }
        catch (Exception ex)
        {
            return Result<BatchProcessingEstimate>.Failure($"Batch estimation failed: {ex.Message}");
        }
    }

    // Private helper methods

    private async Task<Result<FileAnalysis>> AnalyzeInputFilesAsync(string inputPath)
    {
        try
        {
            var files = new List<string>();
            long totalSize = 0;

            if (File.Exists(inputPath))
            {
                files.Add(inputPath);
                totalSize = new FileInfo(inputPath).Length;
            }
            else if (Directory.Exists(inputPath))
            {
                var directoryFiles = Directory.GetFiles(inputPath, "*", SearchOption.AllDirectories)
                    .Where(f => IsHealthcareFile(f))
                    .ToList();
                
                files.AddRange(directoryFiles);
                totalSize = directoryFiles.Sum(f => new FileInfo(f).Length);
            }
            else
            {
                return Result<FileAnalysis>.Failure($"Path not found: {inputPath}");
            }

            // Sample a few files to estimate message density
            var messageCountSample = await EstimateMessageCountAsync(files.Take(5));

            var analysis = new FileAnalysis
            {
                Files = files,
                TotalSizeBytes = totalSize,
                AverageFileSizeBytes = files.Count > 0 ? totalSize / files.Count : 0,
                EstimatedMessagesPerFile = messageCountSample,
                FileTypeDistribution = AnalyzeFileTypes(files)
            };

            return Result<FileAnalysis>.Success(analysis);
        }
        catch (Exception ex)
        {
            return Result<FileAnalysis>.Failure($"File analysis failed: {ex.Message}");
        }
    }

    private async Task<double> EstimateMessageCountAsync(IEnumerable<string> sampleFiles)
    {
        var messageCounts = new List<int>();
        
        foreach (var file in sampleFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file);
                var messageCount = EstimateMessageCount(content);
                messageCounts.Add(messageCount);
            }
            catch
            {
                // If we can't read a file, use default estimate
                messageCounts.Add(EstimatedMessagesPerFile);
            }
        }

        return messageCounts.Any() ? messageCounts.Average() : EstimatedMessagesPerFile;
    }

    private static int EstimateMessageCount(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        // Estimate based on standard message headers
        if (content.Contains("MSH|"))
        {
            // HL7 messages - count MSH segments
            return content.Split("MSH|").Length - 1;
        }
        
        if (content.Contains("\"resourceType\""))
        {
            // FHIR resources - count resourceType occurrences
            return content.Split("\"resourceType\"").Length - 1;
        }
        
        if (content.Contains("<Message>") || content.Contains("<Transaction>"))
        {
            // XML messages - count root elements
            var xmlCount = content.Split("<Message>").Length - 1;
            xmlCount += content.Split("<Transaction>").Length - 1;
            return Math.Max(1, xmlCount);
        }

        // Default fallback
        return 1;
    }

    private static ProcessingEstimate CalculateProcessingEstimate(
        FileAnalysis analysis, 
        DeIdentificationOptions options)
    {
        // Base time calculation
        var baseProcessingTime = TimeSpan.FromMilliseconds(analysis.TotalSizeBytes * MillisecondsPerByte);
        
        // Adjust for complexity factors
        var complexityMultiplier = CalculateComplexityMultiplier(options);
        var adjustedTime = TimeSpan.FromTicks((long)(baseProcessingTime.Ticks * complexityMultiplier));
        
        // Memory estimation
        var baseMemoryBytes = Math.Max(MinimumMemoryMb * BytesPerMegabyte, 
            (long)(analysis.TotalSizeBytes * MemoryMultiplier));
        
        // Throughput calculation
        var totalMessages = analysis.Files.Count * analysis.EstimatedMessagesPerFile;
        var throughput = totalMessages / Math.Max(1, adjustedTime.TotalSeconds);
        
        return new ProcessingEstimate
        {
            EstimatedTime = adjustedTime,
            EstimatedMemoryBytes = baseMemoryBytes,
            FileCount = analysis.Files.Count,
            TotalInputSizeBytes = analysis.TotalSizeBytes,
            EstimatedThroughput = throughput,
            EstimateConfidence = CalculateEstimateConfidence(analysis),
            RecommendedBatchSize = CalculateOptimalBatchSize(analysis.Files.Count, analysis.TotalSizeBytes)
        };
    }

    private static double CalculateComplexityMultiplier(DeIdentificationOptions options)
    {
        var multiplier = 1.0;
        
        // More extensive de-identification takes longer
        if (options.PreserveRelationships)
            multiplier *= 1.3;
        
        if (options.GenerateReport)
            multiplier *= 1.2;
        
        if (options.Method == DeIdentificationMethod.ExpertDetermination)
            multiplier *= 1.5; // Statistical analysis is more complex
        
        return multiplier;
    }

    private static double CalculateEstimateConfidence(FileAnalysis analysis)
    {
        var confidence = 0.8; // Base confidence
        
        // More files = better estimate
        if (analysis.Files.Count > 10)
            confidence += 0.1;
        else if (analysis.Files.Count < 3)
            confidence -= 0.2;
        
        // Consistent file sizes = better estimate
        if (analysis.Files.Count > 1)
        {
            var fileSizes = analysis.Files.Select(f => new FileInfo(f).Length).ToList();
            var avgSize = fileSizes.Average();
            var variance = fileSizes.Sum(s => Math.Pow(s - avgSize, 2)) / fileSizes.Count;
            var stdDev = Math.Sqrt(variance);
            
            if (stdDev < avgSize * 0.2) // Low variance
                confidence += 0.1;
            else if (stdDev > avgSize * 0.8) // High variance
                confidence -= 0.1;
        }
        
        return Math.Max(0.1, Math.Min(1.0, confidence));
    }

    private static int CalculateOptimalBatchSize(int fileCount, long totalSizeBytes)
    {
        if (fileCount <= 1) return 1;
        
        // Target ~100MB per batch for optimal memory usage
        var targetBatchSizeBytes = 100 * BytesPerMegabyte;
        var avgFileSizeBytes = totalSizeBytes / fileCount;
        
        if (avgFileSizeBytes > 0)
        {
            var optimalBatchSize = (int)(targetBatchSizeBytes / avgFileSizeBytes);
            return Math.Max(1, Math.Min(fileCount, optimalBatchSize));
        }
        
        // Fallback to reasonable defaults
        return Math.Min(fileCount, fileCount < 50 ? 10 : fileCount / 10);
    }

    private static double CalculateBatchConfidence(List<ProcessingEstimate> estimates)
    {
        if (!estimates.Any()) return 0.0;
        
        return estimates.Average(e => e.EstimateConfidence);
    }

    private static bool IsHealthcareFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var healthcareExtensions = new[] { ".hl7", ".txt", ".json", ".xml", ".fhir" };
        
        return healthcareExtensions.Contains(extension) || 
               Path.GetFileName(filePath).ToLowerInvariant().Contains("hl7") ||
               Path.GetFileName(filePath).ToLowerInvariant().Contains("fhir");
    }

    private static Dictionary<string, int> AnalyzeFileTypes(List<string> files)
    {
        return files.GroupBy(f => Path.GetExtension(f).ToLowerInvariant())
            .ToDictionary(g => string.IsNullOrEmpty(g.Key) ? "no extension" : g.Key, g => g.Count());
    }

    // Supporting data structures

    private record FileAnalysis
    {
        public required List<string> Files { get; init; }
        public required long TotalSizeBytes { get; init; }
        public required long AverageFileSizeBytes { get; init; }
        public required double EstimatedMessagesPerFile { get; init; }
        public required Dictionary<string, int> FileTypeDistribution { get; init; }
    }

    public record BatchProcessingEstimate
    {
        public required int TotalFiles { get; init; }
        public required long TotalInputSizeBytes { get; init; }
        public required TimeSpan EstimatedTotalTime { get; init; }
        public required long EstimatedPeakMemoryBytes { get; init; }
        public required double EstimatedThroughput { get; init; }
        public required List<ProcessingEstimate> FileEstimates { get; init; }
        public required int RecommendedBatchSize { get; init; }
        public required double EstimateConfidence { get; init; }
    }
}